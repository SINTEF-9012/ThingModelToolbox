using System;
using DotSpatial.Projections;
using ThingModel;

namespace Arma2ThingModel
{
    class Tools
    {
        private static readonly ProjectionInfo _latLngProjection = KnownCoordinateSystems.Geographic.World.WGS1984;
        private static readonly ProjectionInfo _metersProjection = KnownCoordinateSystems.Projected.World.WebMercator;

        public static Location.LatLng ConvertArmaLocationToLatLng(double x, double y, double? altitude = null)
        {
            var p = new Double[2] {x, y};

            Reproject.ReprojectPoints(p, null, _metersProjection, _latLngProjection, 0, 1);

            return new Location.LatLng(p[0], p[1], altitude);
        }
    }
}
