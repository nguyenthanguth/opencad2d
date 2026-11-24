using System;
using System.Collections.Generic;
using GeometryHelper.Dxf;
using GeometryHelper.Geometry;

namespace OpenCAD2D.Remote
{
    /// <summary>What kind of answer the user gave to a question of a run.</summary>
    public enum CadAnswerKind
    {
        /// <summary>Enter with nothing typed and no default: the end of a list, or no answer.</summary>
        None,

        /// <summary>A point: <see cref="CadRemoteAnswer.Point"/>.</summary>
        Point,

        /// <summary>A number: <see cref="CadRemoteAnswer.Number"/>.</summary>
        Number,

        /// <summary>One of the keywords, or the default where it was a keyword: <see cref="CadRemoteAnswer.Text"/> holds it as the prompt writes it.</summary>
        Keyword,

        /// <summary>A text: <see cref="CadRemoteAnswer.Text"/>.</summary>
        Text,

        /// <summary>Objects: <see cref="CadRemoteAnswer.Handles"/> and <see cref="CadRemoteAnswer.Entities"/>.</summary>
        Selection,
    }

    /// <summary>What the user answered to <see cref="CadRemoteRun.Ask"/>.</summary>
    public sealed class CadRemoteAnswer
    {
        private CadRemoteAnswer(CadAnswerKind kind)
        {
            Kind = kind;
        }

        /// <summary>Gets what kind of answer it is.</summary>
        public CadAnswerKind Kind { get; }

        /// <summary>Gets the point of a <see cref="CadAnswerKind.Point"/> answer, in world coordinates, its Z nought.</summary>
        public GeoPoint3 Point { get; private set; }

        /// <summary>Gets the number of a <see cref="CadAnswerKind.Number"/> answer: a distance in millimetres, an angle in radians, or a whole number.</summary>
        public double Number { get; private set; }

        /// <summary>Gets the text of a <see cref="CadAnswerKind.Text"/> or <see cref="CadAnswerKind.Keyword"/> answer; null for the others.</summary>
        public string Text { get; private set; }

        /// <summary>Gets the handles of a <see cref="CadAnswerKind.Selection"/> answer, in the order of the handles; empty for the others.</summary>
        public IReadOnlyList<string> Handles { get; private set; } = Array.Empty<string>();

        /// <summary>Gets copies of the objects of a <see cref="CadAnswerKind.Selection"/> answer, as <see cref="CadRemoteDocument.GetObjects"/> gives them; empty for the others.</summary>
        public IReadOnlyList<DxfEntity> Entities { get; private set; } = Array.Empty<DxfEntity>();

        internal static CadRemoteAnswer Read(CadApplication app, JsonObject answer)
        {
            switch (answer.GetString("kind"))
            {
                case "point":
                    return new CadRemoteAnswer(CadAnswerKind.Point)
                    {
                        Point = new GeoPoint3(answer.GetDouble("x"), answer.GetDouble("y"), answer.GetDouble("z")),
                    };
                case "number":
                    return new CadRemoteAnswer(CadAnswerKind.Number) { Number = answer.GetDouble("number") };
                case "keyword":
                    return new CadRemoteAnswer(CadAnswerKind.Keyword) { Text = answer.GetString("text") };
                case "text":
                    return new CadRemoteAnswer(CadAnswerKind.Text) { Text = answer.GetString("text") };
                case "selection":
                    CadRemoteDocument document = app.Known((JsonObject)answer.Get("document"));
                    var handles = new List<string>();

                    foreach (object handle in answer.GetArray("handles"))
                    {
                        handles.Add((string)handle);
                    }

                    return new CadRemoteAnswer(CadAnswerKind.Selection)
                    {
                        Handles = handles,
                        Entities = document.Objects(answer),
                    };
                default:
                    return new CadRemoteAnswer(CadAnswerKind.None);
            }
        }
    }
}
