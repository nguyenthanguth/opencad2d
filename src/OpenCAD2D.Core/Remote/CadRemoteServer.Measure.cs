using System;
using System.Collections.Generic;
using System.Linq;
using GeometryHelper;
using GeometryHelper.Dxf;
using GeometryHelper.Geometry;
using OpenCAD2D.Remote;

namespace OpenCAD2D
{
    /// <summary>What a thing would take before it is added, the empty places of a frame, and whether a candidate fits, asked from outside.</summary>
    public sealed partial class CadRemoteServer
    {
        private static JsonObject Measure(CadDocument document, JsonObject request)
        {
            string text = request.GetString("fragment");
            DxfDatabase fragment = string.IsNullOrEmpty(text) ? null : DxfDatabase.Parse(text);
            DxfEntity entity = fragment?.ModelSpace.Cast<DxfEntity>().FirstOrDefault(e => !e.IsErased);

            if (entity == null)
            {
                throw new Refusal("A measure comes with the object to measure.");
            }

            return Measured(() => CadMeasure.Of(document, entity));
        }

        private static JsonObject MeasureLeader(CadDocument document, JsonObject request)
        {
            JsonArray arrow = request.GetArray("arrow");
            JsonArray landing = request.GetArray("landing");

            if (arrow.Count != 2 || landing.Count != 2)
            {
                throw new Refusal("A leader to measure has an arrow and a landing, each of two numbers.");
            }

            return Measured(() =>
                CadMeasure.Leader(
                    document,
                    request.GetString("style"),
                    new GeoPoint2(Number(arrow[0]), Number(arrow[1])),
                    new GeoPoint2(Number(landing[0]), Number(landing[1])),
                    request.GetString("words"),
                    request.Get("scale") == null ? (double?)null : request.GetDouble("scale")
                )
            );
        }

        private static JsonObject Measured(Func<CadMeasurement> measure)
        {
            CadMeasurement measured;

            try
            {
                measured = measure();
            }
            catch (ArgumentException exception)
            {
                throw new Refusal(exception.Message);
            }

            var box = new JsonArray();

            if (!measured.Box.IsEmpty)
            {
                box.Add(measured.Box.MinX);
                box.Add(measured.Box.MinY);
                box.Add(measured.Box.MaxX);
                box.Add(measured.Box.MaxY);
            }

            var frames = new JsonArray();

            foreach (CadFrame frame in measured.Frames)
            {
                frames.Add(frame.X);
                frames.Add(frame.Y);
                frames.Add(frame.Width);
                frames.Add(frame.Height);
                frames.Add(frame.Rotation);
            }

            return new JsonObject().Set("box", box).Set("frames", frames);
        }

        private static JsonObject FreeSpace(CadDocument document, JsonObject request)
        {
            JsonArray frame = request.GetArray("frame");

            if (frame.Count != 4)
            {
                throw new Refusal("A frame is four numbers: the least X and Y and the greatest X and Y.");
            }

            CadFreeSpace table;

            try
            {
                table = CadSpatial.FreeSpace(
                    document,
                    SpaceNamed(document, request.GetString("layout")),
                    new DisplayBox(Number(frame[0]), Number(frame[1]), Number(frame[2]), Number(frame[3])),
                    SpaceOptions(request),
                    ToleranceOf(request)
                );
            }
            catch (ArgumentException exception)
            {
                throw new Refusal(exception.Message);
            }
            catch (InvalidOperationException exception)
            {
                throw new Refusal(exception.Message);
            }

            return new JsonObject().Set("boxes", Numbers(table.Boxes));
        }

        private static JsonObject Fits(CadDocument document, JsonObject request)
        {
            JsonArray place = request.GetArray("candidate");

            if (place.Count != 5)
            {
                throw new Refusal(
                    "A candidate is five numbers: its first corner, its width, its height and its angle."
                );
            }

            try
            {
                bool fits = CadSpatial.Fits(
                    document,
                    SpaceNamed(document, request.GetString("layout")),
                    new CadFrame(
                        Number(place[0]),
                        Number(place[1]),
                        Number(place[2]),
                        Number(place[3]),
                        Number(place[4])
                    ),
                    SpaceOptions(request),
                    ToleranceOf(request)
                );
                return new JsonObject().Set("fits", fits);
            }
            catch (ArgumentException exception)
            {
                throw new Refusal(exception.Message);
            }
        }

        private static CadFreeSpaceOptions SpaceOptions(JsonObject request) =>
            new CadFreeSpaceOptions
            {
                Kinds = request.Get("kinds") is JsonArray kinds ? Strings(kinds).ToList() : null,
                Margin = request.GetDouble("margin"),
                MinWidth = request.GetDouble("minWidth"),
                MinHeight = request.GetDouble("minHeight"),
            };

        private static Tolerance ToleranceOf(JsonObject request) =>
            request.Get("tolerance") == null
                ? Touching
                : new Tolerance(request.GetDouble("tolerance"), Tolerance.DefaultEqualVector);

        private static double Number(object value) =>
            value is double real ? real
            : value is long whole ? whole
            : 0.0;
    }
}
