$(document).ready(function() {
	var record = $('<div class="record"></div>'),
		timeline = $('#timeline'),
		jwindow = $(window);

	var myScroll = new IScroll('#wrapper', {
		scrollX: true,
		scrollY: false,
		probeType: 3,
		mouseWheel: true
	});

	var time = $('#time'),
		precisetime = $('#precisetime'),
		currentTime = 0;

	$('#time').text('Loading');

	function updateTime() {
		var m = moment(currentTime);
		time.text(m.fromNow());
		precisetime.text(m.format("H:mm:ss - Do MMMM YYYY"));
	}

	var	oldRecord = null,
		minTime = 0,
		maxTime = 0;

	function WatchScroll() {
		var record = document.elementFromPoint(jwindow.width()/2, jwindow.height()-20);

		if (record && record.className.indexOf("record") >= 0 && oldRecord != record) {
			$(oldRecord).removeClass('selected');
			currentTime = $(record).addClass('selected').data('time');
			updateTime();
			oldRecord = record;
		} 
	}

	var setTime = false;
	myScroll.on('scrollEnd', function() {
		WatchScroll();
		if (setTime) {
			$.get('/set/'+currentTime);
		}
	});
	myScroll.on('scroll', WatchScroll);
		
	$.get('/history/10000', function(data){
		if (!data.length) {
			return;
		}

		minTime = data[0].d;
		maxTime = data[data.length-1].d;

		var previousTime = minTime,
			maxSize = Number.MIN_VALUE,
			minSize = Number.MAX_VALUE;

		data.forEach(function(d) {
			if (d.s > maxSize) {
				maxSize = d.s;
			}
			if (d.s < minSize) {
				minSize = d.s;
			}
		});

		var diffSize = maxSize-minSize
			widthSum = 0;

		data.forEach(function(d) {
			var r = record.clone();
		
			var width = Math.max(Math.min(parseInt((d.d-previousTime)/1000), 99),3),
				// http://en.wikipedia.org/wiki/Sigmoid_function
				height = (1/(1+Math.exp(-d.s/(diffSize/6)))-0.5)*0.9+0.1;

			console.log(diffSize, d.s, height)

			r.css('height', height*100+'%')
			 .css('width',width) 
			 .data('time',d.d);

			previousTime = d.d;
			timeline.append(r);

			widthSum += width;

		});

		timeline.children(':first').width(jwindow.width()/2).height('100%');

		function setMargins() {
			timeline.children(':last, :first').css('margin-right', jwindow.width()/2-1);
		}

		setMargins();

		jwindow.resize(setMargins);

		currentTime = maxTime;

		updateTime();

		myScroll.refresh();
		myScroll.scrollToElement('.record:last-child');
		myScroll.on('scrollEnd', function() {
			setTime = true;
		});

	});

	var playPause = $('#playpause');

	playPause.click(function() {
		$.get('/playpause', function(data) {
			playPause.text(data).blur();
		})
	});

});

