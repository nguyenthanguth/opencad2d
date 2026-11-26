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
            string text = TextMember(request, "fragment");
            DxfDatabase fragment;

            try
            {
                fragment = string.IsNullOrEmpty(text) ? null : DxfDatabase.Parse(text);
            }
            catch (Exception exception) when (!(exception is OutOfMemoryException))
            {
                throw new Refusal("The member fragment is not a drawing: " + exception.Message);
            }

            DxfEntity entity = fragment?.ModelSpace.Cast<DxfEntity>().FirstOrDefault(e => !e.IsErased);

            if (entity == null)
            {
                throw new Refusal("A measure comes with the object to measure.");
            }

            return Measured(() => CadMeasure.Of(document, entity));
        }

        private static JsonObject MeasureLeader(CadDocument document, JsonObject request)
        {
            const string rule = "A leader to measure has an arrow and a landing, each of two numbers.";
            double[] arrow = NumbersMember(request, "arrow", 2, rule);
            double[] landing = NumbersMember(request, "landing", 2, rule);
            string style = TextMember(request, "style");
            string words = TextMember(request, "words");
            double? scale = request.Get("scale") == null ? (double?)null : NumberMember(request, "scale", 0.0);
            return Measured(() =>
                CadMeasure.Leader(
                    document,
                    style,
                    new GeoPoint2(arrow[0], arrow[1]),
                    new GeoPoint2(landing[0], landing[1]),
                    words,
                    scale,
                    Touching
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
            double[] frame = NumbersMember(
                request,
                "frame",
                4,
                "A frame is four numbers: the least X and Y and the greatest X and Y."
            );
            string layout = TextMember(request, "layout");
            CadFreeSpaceOptions options = SpaceOptions(request);
            Tolerance tolerance = ToleranceOf(request);
            CadFreeSpace table;

            try
            {
                table = CadSpatial.FreeSpace(
                    document,
                    SpaceNamed(document, layout),
                    new DisplayBox(frame[0], frame[1], frame[2], frame[3]),
                    options,
                    tolerance
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
            double[] place = NumbersMember(
                request,
                "candidate",
                5,
                "A candidate is five numbers: its first corner, its width, its height and its angle."
            );
            string layout = TextMember(request, "layout");
            CadFreeSpaceOptions options = SpaceOptions(request);
            Tolerance tolerance = ToleranceOf(request);

            try
            {
                bool fits = CadSpatial.Fits(
                    document,
                    SpaceNamed(document, layout),
                    new CadFrame(place[0], place[1], place[2], place[3], place[4]),
                    options,
                    tolerance
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
                Kinds = NamesMember(request, "kinds"),
                Margin = NumberMember(request, "margin", 0.0),
                MinWidth = NumberMember(request, "minWidth", 0.0),
                MinHeight = NumberMember(request, "minHeight", 0.0),
            };

        /// <summary>The tolerance a request asks for, the library's own where it asks for none; refused where it is no number or below nought.</summary>
        private static Tolerance ToleranceOf(JsonObject request)
        {
            if (request.Get("tolerance") == null)
            {
                return Touching;
            }

            double distance = NumberMember(request, "tolerance", 0.0);
            return distance >= 0.0
                ? new Tolerance(distance, Tolerance.DefaultEqualVector)
                : throw new Refusal("The member tolerance has to be a number of millimetres, not below nought.");
        }
    }
}
