var express = require('express'),
    ThingModel = require('thingmodel'),
    morgan = require('morgan'),
    d3 = require('d3'),
    jsdom = require('jsdom'),
    simplify = require('simplify-js'),
    datejs = require('date.js'),
	sqlite3 = require('sqlite3').verbose();

var config = require('./config.json');

var db = new sqlite3.Database(config.database);

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
		' ORDER by datetime ASC'),
	dashboardDb = db.prepare('SELECT d1.value AS thingid, d2.value AS propertykey, recorder.datetime, recorder.value' +
		' FROM recorder, declarations AS d1, declarations AS d2'+
		' WHERE d1.key = recorder.thingid AND d2.key = recorder.propertykey AND datetime >= ?'+
		' ORDER by d1.key, d2.key, datetime ASC'),
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

var warehouse = new ThingModel.Warehouse();

declarationsDb.each(function(error, row) {
	stringDeclarations[row.value] = row.key;
	stringDeclarationsCpt = Math.max(row.key, stringDeclarationsCpt) + 1;
}, function() {
	declarationsDb.reset();
	var thingmodelClient = new ThingModel.WebSockets.Client(config.thingmodel.sender_id, config.thingmodel.endpoint, warehouse); 
});



var onUpdate = function(thing) {
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

warehouse.RegisterObserver({
	New: function(thing) {
		onUpdate(thing);
	},
	Updated: function(thing) {
		onUpdate(thing);
	},
	Define: function(){},
	Deleted: function(){}
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

app.set('port', (process.env.PORT || config.port));

app.get('/:thingId/:propertyKey', function(req, res) {
	var result = [];
	if (!stringDeclarations.hasOwnProperty(req.params.thingId) ||
		!stringDeclarations.hasOwnProperty(req.params.propertyKey)) {
		res.send("lol");
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
			result = simplify(result, 1, true);
		}
		for (var i =0,l=result.length;i<l;++i){
			result[i] = {date:result[i].x, value:result[i].y};
		}
		res.json(result);
	});
});

app.get('/', function(req, res) {
	var data = {}, lastTitle = null, lastDataList = null;
	dashboardDb.each(0, function(error, row){
		var title = row.thingid + " - " + row.propertykey;
		if (lastTitle !== title) {
			lastTitle = title;
			data[title] = lastDataList = [];
		}
		lastDataList.push({x:row.datetime, y:row.value});
	}, function(error) {
		dashboardDb.reset();
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

					window.document.body.appendChild(div);
				}
				/*var view = window.document.getElementById("svg-view");

				/*var line = [
					{x: 1, y: 20},
					{x: 2, y: 22},
					{x: 3, y: 24},
					{x: 4, y: 18},
					{x: 5, y: 25},
					{x: 6, y: 24},
					{x: 7, y: 40},
					{x: 8, y: 25},
					{x: 9, y: 10}
				];

				for (var i = 0; i < 10000;++i) {
					var y = line.length ? Math.round((Math.random()-0.5)*2+line[line.length-1].y) : -40;
					if (y > 20 || y < -20) y = Math.round((Math.random()-0.5)*30);
					line.push({
						x: i/10,
						y: y
					});
				}

				var sLine = simplify(line, 4, true);

				console.log(sLine.length)

				var lineFunction = d3.svg.line()
					.x(function(d) { return d.x; })
					.y(function(d) { return (d.y+20)*10; });
				var sLineFunction = d3.svg.line()
					.x(function(d) { return d.x; })
					.y(function(d) { return (d.y+20)*10; }).interpolate('basis');

				roger.append('path')
					.attr('d', lineFunction(line))
					.attr('stroke', 'blue')
					.attr('stroke-width', 2)
					.attr('fill', 'none');

				roger.append('path')
					.attr('d', sLineFunction(sLine))
					.attr('stroke', 'orange')
					.attr('stroke-width', 2)
					.attr('fill', 'none');*/

				res.type('html');
				res.send(window.document.body.innerHTML);
			}
		});
	});
});

app.listen(app.get('port'), function() {
  console.log("Node app is running at localhost:" + app.get('port'))
});

