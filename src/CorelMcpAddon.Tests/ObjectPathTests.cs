using Xunit;
using CorelMcp.Tools;

namespace CorelMcpAddon.Tests
{
    /// <summary>
    /// Path handling of the object tree: list_objects reports "Layer / Group / Object" and
    /// both set_object_name and the selector's "group" argument parse that same string.
    /// The parsing is pure, so it is tested without CorelDRAW.
    /// </summary>
    public class ObjectPathTests
    {
        [Fact]
        public void SplitPath_TrimsElementsAndDropsEmptyOnes()
        {
            var parts = ShapeTarget.SplitPath("  Flat view / Outer /  Pad 3 ");

            Assert.Equal(new[] { "Flat view", "Outer", "Pad 3" }, parts.ToArray());
        }

        [Fact]
        public void SplitPath_IgnoresLeadingTrailingAndDoubledSeparators()
        {
            Assert.Equal(new[] { "A", "B" }, ShapeTarget.SplitPath("/ A // B /").ToArray());
        }

        [Fact]
        public void SplitPath_OfEmptyOrNullIsEmpty()
        {
            Assert.Empty(ShapeTarget.SplitPath(null));
            Assert.Empty(ShapeTarget.SplitPath(""));
            Assert.Empty(ShapeTarget.SplitPath("   "));
        }

        [Fact]
        public void SplitPath_KeepsNamesThatContainSpacesAndBrackets()
        {
            // "[3]" is meaningful only as a whole element; inside a name it is literal text.
            Assert.Equal(new[] { "Pad [3] left", "[4]" }, ShapeTarget.SplitPath("Pad [3] left / [4]").ToArray());
        }
    }
}
