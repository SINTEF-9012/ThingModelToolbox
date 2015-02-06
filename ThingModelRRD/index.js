var express = require('express'),
    ThingModel = require('thingmodel'),
    morgan = require('morgan'),
    d3 = require('d3'),
    jsdom = require('jsdom'),
    simplify = require('simplify-js'),
    basicAuth = require('basic-auth-connect'),
    datejs = require('date.js'),
	Nedb = require('nedb'),
	sqlite3 = require('sqlite3').verbose();

var config = require('./config.json');

var db = new sqlite3.Database(config.database);
var sourceDb = new Nedb({filename: 'source.db', autoload:true});
var sourcesChannels = {};

sourceDb.find({}, function (err, docs) {
	if (!err) {
		docs.forEach(function(doc){
			sourcesChannels[doc.id] = doc.channel;
		});
	}
});

db.serialize(function() {
	db.run('CREATE TABLE IF NOT EXISTS declarations (key INTEGER PRIMARY KEY, value STRING)');
	db.run('CREATE UNIQUE INDEX IF NOT EXISTS declarationsindex ON declarations(value)');
	db.run('CREATE TABLE IF NOT EXISTS recorder (datetime INTEGER, thingid INTEGER, propertykey INTEGER, value INTEGER)');
	db.run('CREATE INDEX IF NOT EXISTS datetimeindex ON recorder(datetime ASC)');
});

var insertTransactionDb = db.prepare('INSERT INTO recorder VALUES (?, ?, ?, ?)'),
	insertDeclarationDb = db.prepare('INSERT INTO declarations VALUES (?, ?)'),
	findDb = db.prepare('SELECT datetime, value FROM recorder'+
		' WHERE thingid = ? AND propertykey = ? AND datetime >= ? AND datetime <= ?'+
		' ORDER BY datetime ASC'),
	dashboardDb = db.prepare('SELECT d1.value AS thingid, COUNT(*) AS recordscount' +
		' FROM recorder, declarations AS d1' +
		' WHERE d1.key = recorder.thingid' +
		' GROUP BY recorder.thingid' +
		' ORDER BY thingid ASC'),
	thingDb = db.prepare('SELECT d1.value AS thingid, d2.value AS propertykey, recorder.datetime, recorder.value' +
		' FROM recorder, declarations AS d1, declarations AS d2'+
		' WHERE d1.key = recorder.thingid AND d2.key = recorder.propertykey AND datetime >= ? AND d1.value = ?'+
		' ORDER BY d2.value ASC, datetime ASC'),
	declarationsDb = db.prepare('SELECT key, value FROM declarations');

var stringDeclarationsCpt = 1;
var stringDeclarations = {};

var getStringKey = function(value) {
	if (stringDeclarations.hasOwnProperty(value)) {
		return stringDeclarations[value];
	}
	else {
		insertDeclarationDb.run(stringDeclarationsCpt, value);
		stringDeclarations[value] = stringDeclarationsCpt;
		return stringDeclarationsCpt++;
	}
};

var onUpdate = function(thing, channelKey) {
	if (sourcesChannels.hasOwnProperty(thing.ID)) {
		if (sourcesChannels[thing.ID] !== channelKey) {
			sourcesChannels[thing.ID] = channelKey;
			sourceDb.update({id: thing.ID}, {channel: channelKey});
		}
	} else {
		sourcesChannels[thing.ID] = channelKey;
		sourceDb.insert({id: thing.ID, channel: channelKey});
	}

	var date = +new Date();
	thing.Properties.forEach(function(property){
		var value = property._value,
			type = typeof value;
		if (type === 'number') {
			insertTransactionDb.run(date, getStringKey(thing.ID), getStringKey(property.Key), value);
		} else if (type === 'boolean') {
			insertTransactionDb.run(date, getStringKey(thing.ID), getStringKey(property.Key), value ? 1 : 0);
		}
	});
}

var keys = {};

declarationsDb.each(function(error, row) {
	stringDeclarations[row.value] = row.key;
	stringDeclarationsCpt = Math.max(row.key, stringDeclarationsCpt) + 1;
}, function() {
	declarationsDb.reset();

	config.channels.forEach(function(channel) {
		console.log("Loading channel: "+channel.id);
		if (channel.keys) {
			var channelKeys = keys[channel.id] = {};
			channel.keys.forEach(function(k) {
				channelKeys[k] = true;
			})
		}

		var warehouse = new ThingModel.Warehouse();
		var client = new ThingModel.WebSockets.Client(
			config.thingmodel.sender_id,
			config.thingmodel.endpoint+channel.channel, warehouse); 
		warehouse.RegisterObserver({
			New: function(thing) {
				onUpdate(thing, channel.id);
			},
			Updated: function(thing) {
				onUpdate(thing, channel.id);
			},
			Define: function(){},
			Deleted: function(){}
		});
	});
});


var parseDate = function(input) {
	if (input > 0 || input <= 0) {
		var timestamp = parseInt(input, 10);
	} else {
		timestamp = Date.parse(input);
		if (isNaN(timestamp)) {
			timestamp = +datejs(input);
		}
	}

	return timestamp;
}

var app = express();
app.use(morgan('combined'));

var auth = basicAuth("bridge", config.secret);

app.use(function(req, res, next) {
	res.header("Access-Control-Allow-Origin", "*");
	res.header("Access-Control-Allow-Headers", "X-Requested-With");
	next();
});

app.set('port', (process.env.PORT || config.port));

app.get('/', auth, function(req, res) {
	dashboardDb.all(function(error, rows){
		dashboardDb.reset();
		jsdom.env({
			features: { QuerySelector : true },
			html: '<html><head></head><body></body></html>',
			done: function(errors, window) { 
				var doc = window.document;
				var list = doc.createElement("ul");
				for (var row in rows) {
					var r = rows[row];
					var a = doc.createElement("a");
					a.setAttribute("href", "/"+window.encodeURIComponent(r.thingid));
					var count = doc.createElement("em");
					count.appendChild(doc.createTextNode("("+r.recordscount+")"));
					a.appendChild(doc.createTextNode(r.thingid + " "));
					a.appendChild(count);
					var li = doc.createElement("li");
					li.appendChild(a);
					list.appendChild(li);
				}
				doc.body.appendChild(list);
				res.type('html');
				res.send('<!DOCTYPE html>\n<html>'+window.document.body.parentNode.innerHTML+'</html>');
			}
		});
	});
});

app.get('/:thingId', auth, function(req, res) {
	var data = {}, lastTitle = null, lastDataList = null;
	thingDb.each(0, req.params.thingId, function(error, row){
		var title = row.propertykey;
		if (lastTitle !== title) {
			lastTitle = title;
			data[title] = lastDataList = [];
		}
		lastDataList.push({x:row.datetime, y:row.value});
	}, function(error) {
		thingDb.reset();
		jsdom.env({
			features: { QuerySelector : true },
			html: '<html><head></head><body></div></body></html>',
			done: function(errors, window) { 

				for (var key in data) {
					var div = window.document.createElement("div");
					div.className = "dashboard-result";
					var title =  window.document.createElement("h3");
					title.appendChild(window.document.createTextNode(key));
					div.appendChild(title);

					try {
						var sLine = simplify(data[key]);

						var width = 800,
							height = 220,
							margin = 40;

						var roger = d3.select(div).append('svg:svg')
							.attr('width', width+'px')
							.attr('height', height+'px');

						var xRange = d3.scale.linear().range([margin, width-margin]).domain([
							d3.min(sLine, function(d){ return d.x}),
							d3.max(sLine, function(d){ return d.x})]);
						var yRange = d3.scale.linear().range([height-margin, margin]).domain([
							d3.min(sLine, function(d){ return d.y}),
							d3.max(sLine, function(d){ return d.y})]);

						var xAxis = d3.svg.axis().scale(xRange);
						var yAxis = d3.svg.axis().scale(yRange).tickSize(5).orient('left');

						roger.append('svg:g')
							.attr('class', 'x axis')
							.attr('stroke-width', 1)
							.attr('transform', 'translate(0,'+(height-margin)+')')
							.call(xAxis).selectAll("text").remove();

						roger.append('svg:g')
							.attr('class', 'y axis')
							.attr('stroke-width', 1)
							.attr('transform', 'translate('+margin+',0)')
							.call(yAxis);

						//console.log(key,data[key].length);

						var sLineFunction = d3.svg.line()
							.x(function(d) { return xRange(d ? d.x : 0); })
							.y(function(d) { return yRange(d ? d.y : 0); }).interpolate('basis');

						roger.append('path')
							.attr('d', sLineFunction(sLine))
							.attr('stroke', 'orange')
							.attr('stroke-width', 2)
							.attr('fill', 'none');

					} catch(e){}
					window.document.body.appendChild(div);
				}

				res.type('html');
				res.send('<!DOCTYPE html>\n<html>'+window.document.body.parentNode.innerHTML+'</html>');
			}
		});
	});
});

app.get('/:thingId/:propertyKey', function(req, res) {

	var thingId = req.params.thingId;
	if (sourcesChannels.hasOwnProperty(thingId)) {
		var channel = sourcesChannels[thingId];
		if (keys.hasOwnProperty(channel)) {
			var key = req.query.key;
			if (!key || !keys[channel].hasOwnProperty(key)) {
				res.status(403).send('Missing or wrong key');
				return;
			}		
		}	
	}

	var result = [];
	if (!stringDeclarations.hasOwnProperty(thingId) ||
		!stringDeclarations.hasOwnProperty(req.params.propertyKey)) {
		res.status(404).send("Thing not found");
		return;
	}

	var from = req.query.from ? parseDate(req.query.from) : 0,
		to = req.query.to ? parseDate(req.query.to) : 99999999999999; // it's a big timestamp

	findDb.each(stringDeclarations[req.params.thingId],
		stringDeclarations[req.params.propertyKey], from, to, function(error, row) {
		result.push({x:row.datetime, y:row.value});
	}, function(error) {
		findDb.reset();
		if (!req.query.hasOwnProperty("nosimplify")) {
			result = simplify(result, 0.5, true);
		}
		for (var i =0,l=result.length;i<l;++i){
			result[i] = {date:result[i].x, value:result[i].y};
		}
		res.json(result);
	});
});

app.listen(app.get('port'), function() {
  console.log("Node app is running at localhost:" + app.get('port'))
});

