using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;
using static OpenCAD2D.UnitTest.ExportJsonTools;

namespace OpenCAD2D.UnitTest
{
    /// <summary>
    /// The file a standard is written in: its JSON Schema (<see cref="CadStandard.Schema"/>, the file
    /// <c>docs/schemas/opencad2d-standard-v1.schema.json</c>) validates the example standard of the repository and refuses broken
    /// ones, and <see cref="CadStandard.Parse"/> reads what the schema allows and refuses what it does not, in words that name
    /// the member.
    /// </summary>
    public sealed class StandardsSchemaTests
    {
        private static string Example()
        {
            string folder = AppContext.BaseDirectory;

            while (
                folder != null
                && !File.Exists(Path.Combine(folder, "docs", "schemas", "opencad2d-standard-example.json"))
            )
            {
                folder = Path.GetDirectoryName(folder);
            }

            Assert.NotNull(folder);
            return File.ReadAllText(
                Path.Combine(folder, "docs", "schemas", "opencad2d-standard-example.json"),
                System.Text.Encoding.UTF8
            );
        }

        private static List<string> Problems(string json) => Validate(Parse(json), Parse(CadStandard.Schema));

        private static string With(string members) =>
            "{\"schema\":\"opencad2d.standard\",\"version\":1" + (members.Length == 0 ? "" : "," + members) + "}";

        [Fact]
        public void TheSchemaFile_IsJson_OfVersionOne_AndBuiltIntoTheAssembly()
        {
            Dictionary<string, object> schema = Obj(Parse(CadStandard.Schema));

            Assert.Equal("https://json-schema.org/draft/2020-12/schema", schema["$schema"]);
            Assert.Equal("https://opencad2d.com/schemas/opencad2d-standard-v1.schema.json", schema["$id"]);
            Assert.Equal(1.0, (double)Obj(Obj(schema["properties"])["version"])["const"]);
            Assert.Equal(CadStandard.Version, (int)(double)Obj(Obj(schema["properties"])["version"])["const"]);
            Assert.Equal(
                CadStandards.RuleIds.OrderBy(r => r, StringComparer.Ordinal),
                Obj(Obj(Obj(schema["properties"])["weights"])["properties"])
                    .Keys.OrderBy(r => r, StringComparer.Ordinal)
            );
        }

        [Fact]
        public void TheExampleStandard_FitsTheSchema_AndIsRead_WithEveryRuleRun()
        {
            string example = Example();

            Assert.Empty(Problems(example));
            CadStandard standard = CadStandard.Parse(example);
            Assert.Equal("Example company standard", standard.Name);
            Assert.All(CadStandards.RuleIds, rule => Assert.True(standard.Runs(rule), rule));
            Assert.Equal(2.0, standard.WeightOf("text-height"));
            Assert.Equal(1.0, standard.WeightOf("layer-name"));
        }

        [Theory]
        [InlineData("\"layres\":{}", "'layres' is not in the schema")]
        [InlineData("\"layers\":{\"allowed\":[{\"name\":\"A\",\"colour\":1}]}", "'colour' is not in the schema")]
        [InlineData("\"layers\":{\"allowed\":[{\"name\":\"A\",\"color\":300}]}", "fits 0 of the alternatives")]
        [InlineData("\"layers\":{\"allowed\":[{\"name\":\"A\",\"color\":\"red\"}]}", "fits 0 of the alternatives")]
        [InlineData("\"textHeights\":{}", "the member 'paper' is missing")]
        [InlineData("\"textHeights\":{\"paper\":[]}", "fewer than 1 items")]
        [InlineData("\"textHeights\":{\"paper\":[2.5,-1]}", "is not above 0")]
        [InlineData("\"gaps\":{\"wordLine\":-0.5}", "is below 0")]
        [InlineData("\"byLayer\":{\"color\":\"yes\"}", "expected boolean")]
        [InlineData("\"weights\":{\"text-hieght\":2}", "'text-hieght' is not in the schema")]
        [InlineData("\"dimStyles\":[{\"font\":\"romans.shx\"}]", "the member 'name' is missing")]
        public void ABrokenStandard_IsRefusedByTheSchema_AndByTheReader(string members, string schemaSays)
        {
            string json = With(members);

            Assert.Contains(Problems(json), p => p.Contains(schemaSays));
            Assert.Throws<ArgumentException>(() => CadStandard.Parse(json));
        }

        [Theory]
        [InlineData("{\"schema\":\"opencad2d.drawing\",\"version\":1}", "schema")]
        [InlineData("{\"schema\":\"opencad2d.standard\",\"version\":2}", "version")]
        [InlineData("{\"schema\":\"opencad2d.standard\"}", "version")]
        public void AFileOfAnotherSchemaOrVersion_IsRefused_NamingTheMember(string json, string member)
        {
            Assert.NotEmpty(Problems(json));
            ArgumentException refused = Assert.Throws<ArgumentException>(() => CadStandard.Parse(json));
            Assert.Contains("standard." + member, refused.Message);
        }

        [Theory]
        [InlineData("not json", "is not JSON")]
        [InlineData("[1,2]", "JSON object")]
        public void TextThatIsNoStandard_IsRefused(string json, string says)
        {
            ArgumentException refused = Assert.Throws<ArgumentException>(() => CadStandard.Parse(json));
            Assert.Contains(says, refused.Message);
            Assert.Throws<ArgumentNullException>(() => CadStandard.Parse(null));
        }

        [Fact]
        public void TheReaderNamesTheMemberItRefuses()
        {
            ArgumentException a = Assert.Throws<ArgumentException>(() =>
                CadStandard.Parse(With("\"textHeights\":{\"paper\":[2.5],\"tolerance\":\"big\"}"))
            );
            Assert.Contains("standard.textHeights.tolerance", a.Message);
            ArgumentException b = Assert.Throws<ArgumentException>(() =>
                CadStandard.Parse(With("\"weights\":{\"layer-nam\":1}"))
            );
            Assert.Contains("standard.weights.layer-nam", b.Message);
            ArgumentException c = Assert.Throws<ArgumentException>(() =>
                CadStandard.Parse(With("\"layers\":{\"allowed\":[{\"name\":\"A\",\"color\":0}]}"))
            );
            Assert.Contains("standard.layers.allowed[0].color", c.Message);
        }
    }
}
