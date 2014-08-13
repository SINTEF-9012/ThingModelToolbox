/// <reference path="./bower_components/DefinitelyTyped/node/node.d.ts"/> 
/// <reference path="./bower_components/DefinitelyTyped/express/express.d.ts"/> 
/// <reference path="./bower_components/ThingModel/TypeScript/build/ThingModel.d.ts"/>

var argv = require('optimist')
	.usage('ThingModel TimeMachine. \nUsage: $0')
	.demand('database')
	.alias('database', 'db')
	.describe('The SQLITE database file')
	.default('database', 'records.db')
	.demand('endpoint')
	.alias('endpoint', 'ep')
	.describe('The ThingModel server endpoint')
	.default('endpoint', 'ws://localhost:8082/')
	.demand('port')
	.alias('port', 'p')
	.describe('The HTTP listening port')
	.default('port', 4253)
	.boolean('readonly')
	.alias('readonly', 'ro')
	.default('readonly', false)
	.describe('Readonly database')
	.argv;

var sqlite3 = require('sqlite3').verbose();
import express = require('express');
import fs = require('fs');
import vm = require('vm');
import zlib = require('zlib');

// ThingModel is not for NodeJS and you can see it
var includeMoche = (path) => vm.runInThisContext(fs.readFileSync(path).toString(), path);

global.dcodeIO  = {
	ProtoBuf: require('protobufjs')
};

global.WebSocket = require('ws');
global._ = require('underscore');

console.debug = console.log;
console.info = console.info;

includeMoche('./bower_components/ThingModel/TypeScript/build/ThingModel.js');

var db = new sqlite3.Database(argv.database);

db.serialize(function() {
	db.run('CREATE TABLE IF NOT EXISTS recorder (datetime INTEGER, scope BLOB, diff INTEGER)');
	db.run('CREATE TABLE IF NOT EXISTS declarations (key INTEGER PRIMARY KEY, value STRING)');
	db.run('CREATE UNIQUE INDEX IF NOT EXISTS recorderindex ON recorder(datetime)');
//	db.run('CREATE UNIQUE INDEX IF NOT EXISTS declarationsindex ON declarations(value)');
});

var insertTransactionDb = db.prepare('INSERT INTO recorder VALUES (?, ?, ?)'),
	insertDeclarationDb = db.prepare('INSERT INTO declarations VALUES (?, ?)'),
	findDb = db.prepare('SELECT datetime, scope, diff FROM recorder'
		+' WHERE ABS($time-datetime) = (SELECT MIN(ABS($time - datetime)) FROM recorder)'),
	infosDb = db.prepare('SELECT (SELECT COUNT(*) FROM recorder) AS count'
		+', (SELECT datetime FROM recorder'
			+' WHERE datetime = (SELECT MIN(datetime) FROM recorder)) AS oldest'
		+', (SELECT datetime FROM recorder'
			+' WHERE datetime = (SELECT MAX(datetime) FROM recorder)) AS newest'),
	historyDb = db.prepare('SELECT datetime AS d, diff AS s'
		+ ' FROM recorder ORDER BY datetime ASC'),
	declarationsDb = db.prepare('SELECT key, value FROM declarations');

var clientId = "ThingModel TimeMachine";

var differencesAmount = 0;

var stringDeclarationsCpt = 1;
var warehouse = new ThingModel.Warehouse();

var stringDeclarations = {};
var stringToDeclare = {};

var readOnlyDataBase: boolean = argv.readonly;

var save = () => {
	var toProtobuf = new ThingModel.Proto.ToProtobuf();
	var hackToProtobuf = <any>toProtobuf;

	hackToProtobuf._stringDeclarations = /*_.clone(*/stringDeclarations/*)*/;
	hackToProtobuf._stringDeclarationsCpt = stringDeclarationsCpt;
	hackToProtobuf._stringToDeclare = stringToDeclare;

	var transaction = toProtobuf.Convert(
				_.values(warehouse.Things),
				[],
				_.values(warehouse.ThingsTypes),
		clientId);

	_.each(transaction.string_declarations, (d: ThingModel.Proto.StringDeclaration) => {
		insertDeclarationDb.run(d.key, d.value);
//		stringDeclarations[d.value] = d.key;
		stringDeclarationsCpt = Math.max(d.key, stringDeclarationsCpt) + 1;
	});

	transaction.string_declarations = [];

	var data = (<any>transaction).toBuffer();
	var date = +new Date();
	var cpDifferences = Math.round(differencesAmount);
	differencesAmount = 0;

	zlib.gzip(data, (error, result) => {
		if (!error) {
			insertTransactionDb.run(date, result, cpDifferences);
		} else {
			console.log(error);
		}
	});
};

var timeoutId = 0;
var onUpdate = () => {
	if (!readOnlyDataBase && timeoutId === 0) {
		timeoutId = setTimeout(()=> {
			save();
			timeoutId = 0;
		}, 1000);
	}
};

warehouse.RegisterObserver({
	New: () => {
		differencesAmount += 40;
		onUpdate();
	},
	Updated: ()=> {
		differencesAmount += 1;
		onUpdate();
	},
	Define: ()=> {
		differencesAmount += 60;
		onUpdate();
	},
	Deleted: ()=> {
		differencesAmount += 40;
		onUpdate();
	}
});

var client;// : ThingModel.WebSockets.Client;

declarationsDb.each((error, row) => {
	stringDeclarations[row.value] = row.key;
	stringDeclarationsCpt = Math.max(row.key, stringDeclarationsCpt) + 1;
}, ()=> {
	declarationsDb.reset();

	client = new ThingModel.WebSockets.Client(clientId, argv.endpoint, warehouse);

});

var setTransaction = (scope: NodeBuffer)=> {
	zlib.unzip(scope, (error, result)=> {
		if (error) {
			console.log(error);
			return;
		}


		var tmpWarehouse = new ThingModel.Warehouse(); 

		var fromProtobuf = new ThingModel.Proto.FromProtobuf(tmpWarehouse);
		var hackFromProtobuf = <any>fromProtobuf;

		_.each(_.keys(stringDeclarations), (key:string) => {
			hackFromProtobuf._stringDeclarations[stringDeclarations[key]] = key;
		});

		(<any>fromProtobuf).Convert(result);

		readOnlyDataBase = true;
		_.each(warehouse.Things, (thing: ThingModel.Thing) => {
			if (!tmpWarehouse.GetThing(thing.ID)) {
//				console.log("YAAPAAAA",  thing.ID);
				warehouse.RemoveThing(thing);
			}
		});

		_.each(tmpWarehouse.Things, (thing : ThingModel.Thing)=> {
			warehouse.RegisterThing(thing, false, true);
		});
		readOnlyDataBase = argv.readonly;

		client.Send();
	});
};

var currentTime = +new Date(),
	lastRecordTime = 0,
	isPlaying = false,
	speed = 300;

setInterval(() => {
	if (isPlaying) {
		currentTime += speed;


		findDb.get(currentTime, (error, row) => {
			if (lastRecordTime != row.datetime) {
				currentTime = row.datetime;
				lastRecordTime = row.datetime;

				setTransaction(row.scope);
			}
		});
	}
}, speed);


// Express <3
var app = express();

app.get('/history/:precision', (req, res)=> {
	var result = [],
		precision = parseInt(req.params.precision),
		oldTime = 0,
		currentDiff = 0;

	historyDb.each((error, row)=> {
		currentDiff += row.s;
		if (row.d - oldTime > precision) {
			oldTime = row.d;
			row.s = currentDiff;
			currentDiff = 0;
			result.push(row);
		}
	}, ()=> {
		res.send(result);
		historyDb.reset();
	});
}).get('/infos', (req, res)=> {
	infosDb.get((error, row)=> {
		res.send(row);
		infosDb.reset();
	});
}).get('/get/:datetime', (req, res)=> {
	findDb.get(parseInt(req.params.datetime), (error, row)=> {
		res.send(row);
	});
}).get('/set/:datetime', (req, res)=> {
	findDb.get(parseInt(req.params.datetime), (error, row)=> {
		currentTime = row.datetime;
		setTransaction(row.scope);
		res.send("ok");
	});
}).get('/play', (req, res)=> {
	isPlaying = true;
	res.send("ok");
}).get('/pause', (req, res)=> {
	isPlaying = false;
	res.send("ok");
}).get('/playpause', (req, res)=> {
	isPlaying = !isPlaying;
	res.send(isPlaying ? "pause" : "play");
});

app.use(express.static('public'));
app.use(express.compress());

var port = parseInt(argv.port);
app.listen(port ? port : 4253);