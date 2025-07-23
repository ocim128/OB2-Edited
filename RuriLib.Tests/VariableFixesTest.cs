using System;
using System.Collections.Generic;
using RuriLib.Helpers;
using RuriLib.Helpers.CSharp;
using RuriLib.Models.Variables;
using Xunit;

namespace RuriLib.Tests
{
    public class VariableFixesTest
    {
        [Fact]
        public void TestVariableNamesPerformance()
        {
            // Test performance improvement in MakeValid
            string invalidName = "123invalid-name@#$";
            string validName = VariableNames.MakeValid(invalidName);
            Assert.Equal("_123invalid_name_", validName);

            // Test null handling
            Assert.Throws<ArgumentNullException>(() => VariableNames.MakeValid(null));

            // Test empty string handling
            string randomName = VariableNames.MakeValid("");
            Assert.NotEmpty(randomName);
            Assert.Matches("^[a-z]{4}$", randomName);
        }

        [Fact]
        public void TestStringVariableBoolConversion()
        {
            // Test improved boolean conversion logic
            var trueVar = new StringVariable("1");
            Assert.True(trueVar.AsBool());

            var falseVar = new StringVariable("0");
            Assert.False(falseVar.AsBool());

            var yesVar = new StringVariable("yes");
            Assert.True(yesVar.AsBool());

            var noVar = new StringVariable("no");
            Assert.False(noVar.AsBool());

            var emptyVar = new StringVariable("");
            Assert.False(emptyVar.AsBool());

            var trueTextVar = new StringVariable("true");
            Assert.True(trueTextVar.AsBool());

            var falseTextVar = new StringVariable("false");
            Assert.False(falseTextVar.AsBool());
        }

        [Fact]
        public void TestVariableDetectorSecurity()
        {
            // Test ReDoS protection - should not hang on malicious input
            string maliciousInput = new string('<', 10000) + "test" + new string('>', 10000);
            var result = VariableDetector.DetectFromInterpolatedString(maliciousInput);
            Assert.Empty(result);

            // Test normal functionality still works
            string normalInput = "Hello <username>, your balance is <balance>";
            result = VariableDetector.DetectFromInterpolatedString(normalInput);
            Assert.Contains("username", result);
            Assert.Contains("balance", result);

            // Test LoliCode detection
            string loliCode = "value = $<test> and @user.name";
            var loliResult = VariableDetector.DetectFromLoliCodeStatement(loliCode);
            Assert.Contains("test", loliResult);
            Assert.Contains("user", loliResult);

            // Test expression detection
            string expression = "if (count > 0 && user.active)";
            var exprResult = VariableDetector.DetectFromExpression(expression);
            Assert.Contains("count", exprResult);
            Assert.Contains("user", exprResult);
        }

        [Fact]
        public void TestVariableNamesValidation()
        {
            // Test IsValid method improvements
            Assert.True(VariableNames.IsValid("validName"));
            Assert.True(VariableNames.IsValid("valid_name"));
            Assert.True(VariableNames.IsValid("Valid123"));
            Assert.False(VariableNames.IsValid("123invalid"));
            Assert.False(VariableNames.IsValid(""));
            Assert.False(VariableNames.IsValid(null));
            Assert.True(VariableNames.IsValid("data.source"));
            Assert.False(VariableNames.IsValid("data.123invalid"));
        }
    }
}