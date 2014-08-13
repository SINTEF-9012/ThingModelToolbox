removeAllWeapons player;


hint("Arma2Net.Unmanaged" callExtension "Reload");

// INIT:endpoint|lat|lng
"Arma2Net.Unmanaged" callExtension "Arma2ThingModel INIT|Arma|ws://localhost:8082|39.905572|25.221947|14258|15819|0.75";

while {(true)} do
{
	data = [];
	
	{ data = data + [[_x]+getPos(_x)+(velocity _x)+[getDir(_x),getDammage(_x),getFatigue(_x),(fuel _x)]] } forEach allUnits;
	
	_a = "Arma2Net.Unmanaged" callExtension format ["Arma2ThingModel %1", data];
	
	switch(_a) do
	{
		case "nop":
		{
			// do nothing
		};

		case "connected":
		{
			hintSilent "Connected"
		};

		default
		{
			// Execute the string
			call compile _a;
		};
	};
	
	sleep 0.5;
	
};