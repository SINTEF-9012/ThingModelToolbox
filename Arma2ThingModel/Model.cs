using ThingModel;
using ThingModel.Builders;

namespace Arma2ThingModel
{
    class Model
    {
        public static readonly ThingType ArmaThing =
            BuildANewThingType.Named("ArmaThing")
                .ContainingA.LocationLatLng()
                //.AndA.NotRequired.LocationPoint("gameLocation")
                .AndA.Double("heading")
                .AndA.Double("speed")
                .AndA.Double("dammage");

        public static readonly ThingType ArmaCamera =
            BuildANewThingType.Named("ArmaCamera")
                .ContainingA.String("view")
                .AndA.String("target")
                .AndA.String("computerName");

        public static readonly ThingType GGSQuestion =
            BuildANewThingType.Named("GGSQuestion")
                .ContainingA.String("question")
                .AndA.NotRequired.String("answer");

        public static readonly ThingType ArmaVehicle =
            BuildANewThingType.Named("ArmaVehicle")
                .ContainingA.CopyOf(ArmaThing)
                .AndA.Double("fuel");

        public static readonly ThingType ArmaUAV =
            BuildANewThingType.Named("ArmaUAV")
                .ContainingA.CopyOf(ArmaVehicle);
        
        public static readonly ThingType ArmaUGV =
            BuildANewThingType.Named("ArmaUGV")
                .ContainingA.CopyOf(ArmaVehicle)
                .AndA.Boolean("waiting");
        
        public static readonly ThingType ArmaUnit =
            BuildANewThingType.Named("ArmaUnit")
                .ContainingA.CopyOf(ArmaThing)
                .AndA.Double("fatigue");
        
        public static readonly ThingType ArmaPlayer =
            BuildANewThingType.Named("ArmaPlayer")
                .ContainingA.CopyOf(ArmaUnit);
        
        public static readonly ThingType ArmaDetectedUnit =
            BuildANewThingType.Named("ArmaDetectedUnit")
                .ContainingA.CopyOf(ArmaUnit)
                .AndA.String("side");

        public static readonly ThingType ArmaMarker =
            BuildANewThingType.Named("ArmaMarker")
                .ContainingA.LocationLatLng()
                .AndA.String("type")
                .AndA.String("text");
    }
}
