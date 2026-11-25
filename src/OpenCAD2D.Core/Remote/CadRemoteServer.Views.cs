using System;
using System.Collections.Generic;
using GeometryHelper.Dxf;
using GeometryHelper.Geometry;
using OpenCAD2D.Remote;

namespace OpenCAD2D
{
    /// <summary>The view, the named views, the system variables and the selection by area: the calls of a drawing's <see cref="CadEditor"/> from outside.</summary>
    public sealed partial class CadRemoteServer
    {
        /// <summary>The editor of a drawing's session; for a request that moves the view, the drawing has to be the one shown, whose session holds the canvas's view.</summary>
        private CadEditor EditorOf(CadDocument document, bool moves)
        {
            if (moves && !ReferenceEquals(document, _host.ActiveDocument))
            {
                throw new Refusal("The drawing " + document.Title + " is not the drawing shown: activate it first.");
            }

            return _host.SessionOf(document).Editor;
        }

        /// <summary>Does what the editor is asked, a refusal of its own (a bad argument, a call that cannot be made now) told to the client in its words.</summary>
        private static T Edit<T>(Func<T> work)
        {
            try
            {
                return work();
            }
            catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException)
            {
                // The name of the parameter that .NET adds on a second line is the server's, not the client's.
                string message = ex.Message;
                int line = message.IndexOfAny(new[] { '\r', '\n' });
                throw new Refusal(line < 0 ? message : message.Substring(0, line));
            }
        }

        private static GeoPoint2 PointOf(JsonObject request, string name)
        {
            JsonObject at = request.GetObject(name);

            if (at == null)
            {
                throw new Refusal("The request " + request.GetString("op") + " needs " + name + ": a point.");
            }

            return new GeoPoint2(at.GetDouble("x"), at.GetDouble("y"));
        }

        private static JsonObject Corner(DisplayBox box, bool least) =>
            new JsonObject().Set("x", least ? box.MinX : box.MaxX).Set("y", least ? box.MinY : box.MaxY).Set("z", 0.0);

        private JsonObject Zoom(CadDocument document, JsonObject request)
        {
            CadEditor editor = EditorOf(document, true);
            string how = request.GetString("how");
            bool moved = true;

            Edit(() =>
            {
                switch (how)
                {
                    case "extents":
                        editor.ZoomExtents();
                        break;
                    case "window":
                        editor.ZoomWindow(PointOf(request, "first"), PointOf(request, "second"));
                        break;
                    case "objects":
                        var handles = new List<string>();

                        foreach (object item in request.GetArray("handles"))
                        {
                            if (item is string handle)
                            {
                                handles.Add(handle);
                            }
                        }

                        editor.ZoomObjects(handles);
                        break;
                    case "center":
                        editor.ZoomCenter(PointOf(request, "center"), request.GetDouble("height"));
                        break;
                    case "scaled":
                        editor.ZoomScaled(request.GetDouble("factor"));
                        break;
                    case "previous":
                        moved = editor.ZoomPrevious();
                        break;
                    default:
                        throw new Refusal("OpenCAD2D does not know the zoom " + how + ": it is of an older version.");
                }

                return 0;
            });
            return new JsonObject().Set("moved", moved);
        }

        private JsonObject Pan(CadDocument document, JsonObject request)
        {
            CadEditor editor = EditorOf(document, true);
            Edit(() =>
            {
                editor.Pan(new GeoVector2(request.GetDouble("dx"), request.GetDouble("dy")));
                return 0;
            });
            return new JsonObject();
        }

        private JsonObject Regen(CadDocument document)
        {
            EditorOf(document, false).Regen();
            return new JsonObject();
        }

        private JsonObject View(CadDocument document, JsonObject request)
        {
            CadEditor editor = EditorOf(document, true);

            if (request.Get("min") != null || request.Get("max") != null)
            {
                GeoPoint2 least = PointOf(request, "min");
                GeoPoint2 greatest = PointOf(request, "max");
                Edit(() =>
                {
                    editor.SetCurrentView(new DisplayBox(least.X, least.Y, greatest.X, greatest.Y));
                    return 0;
                });
            }

            DisplayBox view = editor.GetCurrentView();
            return new JsonObject()
                .Set("min", Corner(view, true))
                .Set("max", Corner(view, false))
                .Set("window", editor.HasWindow);
        }

        private JsonObject NamedViews(CadDocument document)
        {
            var names = new JsonArray();

            foreach (string name in EditorOf(document, false).NamedViews)
            {
                names.Add(name);
            }

            return new JsonObject().Set("names", names);
        }

        private JsonObject NamedView(CadDocument document, string op, string name)
        {
            CadEditor editor = EditorOf(document, op != RemoteOps.DeleteView);
            Edit(() =>
            {
                switch (op)
                {
                    case RemoteOps.SaveView:
                        editor.SaveView(name);
                        break;
                    case RemoteOps.RestoreView:
                        editor.RestoreView(name);
                        break;
                    default:
                        editor.DeleteView(name);
                        break;
                }

                return 0;
            });
            return new JsonObject();
        }

        private JsonObject Variable(CadDocument document, JsonObject request)
        {
            CadEditor editor = EditorOf(document, false);
            string name = request.GetString("name") ?? string.Empty;
            object given = request.Get("value");

            if (given != null)
            {
                object value =
                    given is string text ? text
                    : given is long whole
                        ? (whole >= int.MinValue && whole <= int.MaxValue ? (object)(int)whole : whole)
                    : given is double real ? real
                    : given is JsonArray list ? NumbersOf(list)
                    : null;

                if (value == null)
                {
                    throw new Refusal("The value of the variable " + name + " is a number, a text or a point.");
                }

                Edit(() =>
                {
                    editor.SetVariable(name, value);
                    return 0;
                });
            }

            object now = Edit(() => editor.GetVariable(name));
            var answer = new JsonObject();

            switch (now)
            {
                case int whole:
                    return answer.Set("kind", "integer").Set("value", (long)whole);
                case double[] point:
                    var numbers = new JsonArray();

                    foreach (double coordinate in point)
                    {
                        numbers.Add(coordinate);
                    }

                    return answer.Set("kind", "point").Set("value", numbers);
                case double real:
                    return answer.Set("kind", "real").Set("value", real);
                default:
                    return answer.Set("kind", "text").Set("value", now as string ?? string.Empty);
            }
        }

        /// <summary>The two or three numbers of a point written as an array.</summary>
        private static double[] NumbersOf(JsonArray list)
        {
            if (list.Count != 2 && list.Count != 3)
            {
                throw new Refusal("A point is two or three numbers.");
            }

            var numbers = new double[list.Count];

            for (int i = 0; i < numbers.Length; i++)
            {
                numbers[i] =
                    list[i] is double real ? real
                    : list[i] is long whole ? whole
                    : throw new Refusal("A point is two or three numbers.");
            }

            return numbers;
        }

        private JsonObject SelectArea(CadDocument document, JsonObject request)
        {
            CadEditor editor = EditorOf(document, false);
            string how = request.GetString("how");
            IReadOnlyList<string> handles = Edit(() =>
            {
                switch (how)
                {
                    case "all":
                        return editor.SelectAll();
                    case "window":
                        return editor.SelectWindow(PointOf(request, "first"), PointOf(request, "second"));
                    case "crossing":
                        return editor.SelectCrossingWindow(PointOf(request, "first"), PointOf(request, "second"));
                    default:
                        throw new Refusal(
                            "OpenCAD2D does not know the selection " + how + ": it is of an older version."
                        );
                }
            });
            var entities = new List<DxfEntity>();

            foreach (string handle in handles)
            {
                DxfEntity entity = document.FindEntity(handle);

                if (entity != null && !entity.IsErased)
                {
                    entities.Add(entity);
                }
            }

            return Pack(document, entities);
        }
    }
}
