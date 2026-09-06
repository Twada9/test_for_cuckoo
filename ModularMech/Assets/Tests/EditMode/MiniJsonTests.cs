using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using ModularMech.Serialization;
using NUnit.Framework;

namespace ModularMech.Tests
{
    /// <summary>
    /// 依存ゼロの MiniJson 実装を固定する。UnityEngine.JsonUtility が Dictionary を
    /// 扱えないため自前実装した経緯があるので(CLAUDE.md 規約8)、まさにその
    /// Dictionary/List 往復が壊れていないことを重点的に見る。
    /// </summary>
    [TestFixture]
    public sealed class MiniJsonTests
    {
        [Test]
        public void Deserialize_ObjectLiteral_ReturnsDictionaryWithParsedValues()
        {
            var result = MiniJson.Deserialize("{\"a\": 1, \"b\": \"text\", \"c\": true}");

            var dict = result as Dictionary<string, object>;
            Assert.That(dict, Is.Not.Null);
            Assert.That(dict["a"], Is.EqualTo(1.0));
            Assert.That(dict["b"], Is.EqualTo("text"));
            Assert.That(dict["c"], Is.EqualTo(true));
        }

        [Test]
        public void Deserialize_ArrayLiteral_ReturnsListOfValues()
        {
            var result = MiniJson.Deserialize("[1, 2.5, \"x\", false, null]");

            var list = result as List<object>;
            Assert.That(list, Is.Not.Null);
            Assert.That(list.Count, Is.EqualTo(5));
            Assert.That(list[0], Is.EqualTo(1.0));
            Assert.That(list[1], Is.EqualTo(2.5));
            Assert.That(list[2], Is.EqualTo("x"));
            Assert.That(list[3], Is.EqualTo(false));
            Assert.That(list[4], Is.Null);
        }

        [Test]
        public void Deserialize_String_ReturnsPlainString()
        {
            Assert.That(MiniJson.Deserialize("\"hello\""), Is.EqualTo("hello"));
        }

        [TestCase("0", 0.0)]
        [TestCase("42", 42.0)]
        [TestCase("-7", -7.0)]
        [TestCase("3.5", 3.5)]
        [TestCase("-0.25", -0.25)]
        [TestCase("1e3", 1000.0)]
        [TestCase("1.5e-2", 0.015)]
        public void Deserialize_Number_ReturnsDoubleRegardlessOfNotation(string token, double expected)
        {
            var result = MiniJson.Deserialize(token);
            Assert.That(result, Is.InstanceOf<double>());
            Assert.That((double)result, Is.EqualTo(expected).Within(1e-9));
        }

        [Test]
        public void Deserialize_BooleanLiterals_ReturnBool()
        {
            Assert.That(MiniJson.Deserialize("true"), Is.EqualTo(true));
            Assert.That(MiniJson.Deserialize("false"), Is.EqualTo(false));
        }

        [Test]
        public void Deserialize_NullLiteral_ReturnsNull()
        {
            Assert.That(MiniJson.Deserialize("null"), Is.Null);
        }

        [Test]
        public void Deserialize_EscapedCharacters_AreUnescapedCorrectly()
        {
            // 元の文字列: a"b\c<TAB>d<LF>e
            var json = "\"a\\\"b\\\\c\\td\\ne\"";
            var result = MiniJson.Deserialize(json) as string;

            Assert.That(result, Is.EqualTo("a\"b\\c\td\ne"));
        }

        [Test]
        public void Deserialize_UnicodeEscape_DecodesToCharacter()
        {
            // \u3042 は "あ"。
            var result = MiniJson.Deserialize("\"\\u3042\"") as string;
            Assert.That(result, Is.EqualTo("あ"));
        }

        [Test]
        public void Deserialize_RawUnicodeCharactersInString_PassThroughUnchanged()
        {
            var result = MiniJson.Deserialize("\"日本語テキスト\"") as string;
            Assert.That(result, Is.EqualTo("日本語テキスト"));
        }

        [Test]
        public void Deserialize_NumbersUseInvariantCulture_EvenUnderCommaDecimalCulture()
        {
            RunUnderCulture("de-DE", () =>
            {
                var result = MiniJson.Deserialize("1.5");
                Assert.That(result, Is.InstanceOf<double>());
                Assert.That((double)result, Is.EqualTo(1.5).Within(1e-9),
                    "現在のスレッドカルチャがカンマ小数区切りでも InvariantCulture で解釈すること");
            });
        }

        [Test]
        public void Serialize_NumbersUseInvariantCulture_EvenUnderCommaDecimalCulture()
        {
            RunUnderCulture("de-DE", () =>
            {
                var json = MiniJson.Serialize(1.5);
                Assert.That(json, Is.EqualTo("1.5"), "カンマではなくピリオドで出力すること(InvariantCulture)");
            });
        }

        [TestCase("{\"a\": 1,}")]              // 末尾カンマ
        [TestCase("{\"a\": }")]                 // 値が無い
        [TestCase("\"unterminated")]            // 文字列が閉じていない
        [TestCase("{\"a\": 1} garbage")]        // ルート値の後ろに余剰トークン
        [TestCase("[1, 2")]                     // 配列が閉じていない
        [TestCase("tru")]                       // リテラルが壊れている
        [TestCase("")]                          // 空文字列
        [TestCase("   ")]                       // 空白のみ
        public void Deserialize_MalformedJson_ReturnsNullInsteadOfThrowing(string json)
        {
            object result = null;
            Assert.DoesNotThrow(() => result = MiniJson.Deserialize(json));
            Assert.That(result, Is.Null);
        }

        [Test]
        public void Deserialize_NullInput_ReturnsNull()
        {
            Assert.That(MiniJson.Deserialize(null), Is.Null);
        }

        [Test]
        public void Deserialize_DeeplyNestedJsonBeyondLimit_ReturnsNullInsteadOfCrashing()
        {
            // 実装の MaxDepth = 64。100 階層のネストは上限を超えるため、
            // StackOverflow ではなく null を返すことを固定する(壊れたJSON = 例外を投げない、の一種)。
            var json = string.Concat(Enumerable.Repeat("[", 100)) + "1" + string.Concat(Enumerable.Repeat("]", 100));

            object result = null;
            Assert.DoesNotThrow(() => result = MiniJson.Deserialize(json));
            Assert.That(result, Is.Null);
        }

        [Test]
        public void Deserialize_NestedJsonWithinLimit_ParsesSuccessfully()
        {
            var json = string.Concat(Enumerable.Repeat("[", 10)) + "1" + string.Concat(Enumerable.Repeat("]", 10));

            var result = MiniJson.Deserialize(json);

            // 10段掘って中身が 1.0 であることまで確認する。
            object cursor = result;
            for (int i = 0; i < 10; i++)
            {
                var list = cursor as List<object>;
                Assert.That(list, Is.Not.Null, $"深さ {i} でリストが取得できない");
                Assert.That(list.Count, Is.EqualTo(1));
                cursor = list[0];
            }
            Assert.That(cursor, Is.EqualTo(1.0));
        }

        [Test]
        public void RoundTrip_SerializeThenDeserialize_ProducesEquivalentStructure()
        {
            var original = new Dictionary<string, object>
            {
                ["name"] = "テスト機体\"1\"",
                ["count"] = 3.0,
                ["ratio"] = 0.7,
                ["active"] = true,
                ["backpack"] = null,
                ["tags"] = new List<object> { "a", "b", 1.0, false, null },
                ["nested"] = new Dictionary<string, object>
                {
                    ["inner"] = "value\nwith\tescapes",
                },
            };

            var json = MiniJson.Serialize(original, pretty: true);
            var roundTripped = MiniJson.Deserialize(json) as Dictionary<string, object>;

            Assert.That(roundTripped, Is.Not.Null);
            Assert.That(roundTripped["name"], Is.EqualTo(original["name"]));
            Assert.That(roundTripped["count"], Is.EqualTo(original["count"]));
            Assert.That(roundTripped["ratio"], Is.EqualTo(original["ratio"]));
            Assert.That(roundTripped["active"], Is.EqualTo(original["active"]));
            Assert.That(roundTripped["backpack"], Is.Null);

            var tags = roundTripped["tags"] as List<object>;
            Assert.That(tags, Is.Not.Null);
            Assert.That(tags, Is.EqualTo(new List<object> { "a", "b", 1.0, false, null }));

            var nested = roundTripped["nested"] as Dictionary<string, object>;
            Assert.That(nested, Is.Not.Null);
            Assert.That(nested["inner"], Is.EqualTo("value\nwith\tescapes"));
        }

        [Test]
        public void Serialize_EscapesQuotesAndBackslashesAndControlCharacters()
        {
            var value = "a\"b\\c\nd\te";
            var json = MiniJson.Serialize(value);

            // 出力そのものをパーサ抜きで確認(手計算): 各特殊文字が2文字のエスケープ列になる。
            Assert.That(json, Is.EqualTo("\"a\\\"b\\\\c\\nd\\te\""));

            // かつ MiniJson 自身での往復でも元に戻ることを確認する。
            var roundTripped = MiniJson.Deserialize(json) as string;
            Assert.That(roundTripped, Is.EqualTo(value));
        }

        /// <summary>
        /// カレントスレッドのカルチャを一時的に切り替えて処理を実行し、必ず元に戻す。
        /// 他のテストへ副作用を漏らさないため try/finally で確実に復元する。
        /// </summary>
        private static void RunUnderCulture(string cultureName, Action action)
        {
            var previous = Thread.CurrentThread.CurrentCulture;
            try
            {
                Thread.CurrentThread.CurrentCulture = new CultureInfo(cultureName);
                action();
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = previous;
            }
        }
    }
}
