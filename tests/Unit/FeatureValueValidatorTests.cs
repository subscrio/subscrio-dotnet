using FluentAssertions;
using Subscrio.Core.Application.Errors;
using Subscrio.Core.Application.Utils;
using Subscrio.Core.Domain.ValueObjects;
using Xunit;

namespace Subscrio.Core.Tests.Unit;

public class FeatureValueValidatorTests
{
    public class ToggleFeatures
    {
        [Fact]
        public void ValidatesTrueValue()
        {
            Action act = () => FeatureValueValidator.Validate("true", FeatureValueType.Toggle);
            act.Should().NotThrow();
        }

        [Fact]
        public void ValidatesFalseValue()
        {
            Action act = () => FeatureValueValidator.Validate("false", FeatureValueType.Toggle);
            act.Should().NotThrow();
        }

        [Fact]
        public void ValidatesTrueCaseInsensitive()
        {
            Action act = () => FeatureValueValidator.Validate("TRUE", FeatureValueType.Toggle);
            act.Should().NotThrow();
        }

        [Fact]
        public void ValidatesFalseCaseInsensitive()
        {
            Action act = () => FeatureValueValidator.Validate("False", FeatureValueType.Toggle);
            act.Should().NotThrow();
        }

        [Fact]
        public void ThrowsForInvalidToggleValue()
        {
            Action act = () => FeatureValueValidator.Validate("maybe", FeatureValueType.Toggle);
            act.Should().Throw<ValidationException>();
        }

        [Fact]
        public void ThrowsForNumericToggleValue()
        {
            Action act = () => FeatureValueValidator.Validate("1", FeatureValueType.Toggle);
            act.Should().Throw<ValidationException>();
        }
    }

    public class NumericFeatures
    {
        [Fact]
        public void ValidatesPositiveInteger()
        {
            Action act = () => FeatureValueValidator.Validate("42", FeatureValueType.Numeric);
            act.Should().NotThrow();
        }

        [Fact]
        public void ValidatesZero()
        {
            Action act = () => FeatureValueValidator.Validate("0", FeatureValueType.Numeric);
            act.Should().NotThrow();
        }

        [Fact]
        public void ValidatesNegativeNumber()
        {
            Action act = () => FeatureValueValidator.Validate("-5", FeatureValueType.Numeric);
            act.Should().NotThrow();
        }

        [Fact]
        public void ValidatesDecimalNumber()
        {
            Action act = () => FeatureValueValidator.Validate("3.14", FeatureValueType.Numeric);
            act.Should().NotThrow();
        }

        [Fact]
        public void ThrowsForNonNumericValue()
        {
            Action act = () => FeatureValueValidator.Validate("not-a-number", FeatureValueType.Numeric);
            act.Should().Throw<ValidationException>();
        }

        [Fact]
        public void ThrowsForNaN()
        {
            Action act = () => FeatureValueValidator.Validate("NaN", FeatureValueType.Numeric);
            act.Should().Throw<ValidationException>();
        }

        [Fact]
        public void ThrowsForInfinity()
        {
            Action act = () => FeatureValueValidator.Validate("Infinity", FeatureValueType.Numeric);
            act.Should().Throw<ValidationException>();
        }
    }

    public class TextFeatures
    {
        [Fact]
        public void ValidatesAnyStringValue()
        {
            Action act = () => FeatureValueValidator.Validate("any text", FeatureValueType.Text);
            act.Should().NotThrow();
        }

        [Fact]
        public void ValidatesEmptyString()
        {
            Action act = () => FeatureValueValidator.Validate("", FeatureValueType.Text);
            act.Should().NotThrow();
        }

        [Fact]
        public void ValidatesSpecialCharacters()
        {
            Action act = () => FeatureValueValidator.Validate("!@#$%^&*()", FeatureValueType.Text);
            act.Should().NotThrow();
        }

        [Fact]
        public void ValidatesUnicodeCharacters()
        {
            Action act = () => FeatureValueValidator.Validate("🚀 emoji text", FeatureValueType.Text);
            act.Should().NotThrow();
        }
    }
}


