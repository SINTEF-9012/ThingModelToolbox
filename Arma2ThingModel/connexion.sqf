removeAllWeapons player;


hint("Arma2Net.Unmanaged" callExtension "Reload");

sleep 2;

// INIT:hostname:port:lat:lng
"Arma2Net.Unmanaged" callExtension "Arma2ThingModel INIT|Arma|wss://master-bridge.eu/thingmodel";

thingmodelPingCpt=0;

while {(true)} do
{
	_a = "nop";
	if (thingmodelPingCpt == 0) then {
		units_data = [];
		{ units_data = units_data + [[_x]+getPos(_x)+[getDir(_x),getDammage(_x),getFatigue(_x),(fuel vehicle _x),(speed _x),(typeOf vehicle _x),(isPlayer _x),(side _x),(vehicle _x), (_x getVariable "detectedBy")]] } forEach allUnits;

		markers_data = [];
		{ markers_data = markers_data + [[_x]+markerPos(_x)+[markerType(_x),markerText(_x)]] } forEach allMapMarkers;
		
		_a = "Arma2Net.Unmanaged" callExtension format ["Arma2ThingModel %1|%2", units_data, markers_data];

		thingmodelPingCpt = 9;
	} else {
		thingmodelPingCpt = thingmodelPingCpt-1;
		_a = "Arma2Net.Unmanaged" callExtension "Arma2ThingModel PING";
	};
	
	
	switch(_a) do
	{
		case "pong":
		{
			// do nothing
		};

		case "nop":
		{
			// do nothing
		};

		case "connected":
		{
			hint "Connected"
		};

		case "notconnected":
		{
			hint "Not connected"
		};

		default
		{
			// Execute the string
			call compile _a;
		};
	};
	
	sleep 0.05;
	
};