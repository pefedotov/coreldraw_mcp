using System;
using System.IO;
using CorelMcp.Config;
using CorelMcp.Tools;
using Xunit;

namespace CorelMcpAddon.Tests
{
    /// <summary>
    /// AllowedRoots enforcement. The cases are the ones that decide whether the documented
    /// restriction is real: a sibling directory that shares the root's prefix, a ".." escape,
    /// and case differences on Windows paths.
    /// </summary>
    public class PathPolicyTests
    {
        private static AddonConfig Config(string roots)
        {
            return new AddonConfig { AllowedRoots = roots };
        }

        [Fact]
        public void EmptyRoots_AllowsAnyPath()
        {
            var path = PathPolicy.EnsureAllowed(@"C:\Windows\System32\x.txt", "path", Config(""));

            Assert.Equal(Path.GetFullPath(@"C:\Windows\System32\x.txt"), path);
        }

        [Fact]
        public void PathInsideRoot_IsReturnedCanonical()
        {
            var path = PathPolicy.EnsureAllowed(@"C:\out\sub\..\drawing.png", "path", Config(@"C:\out"));

            Assert.Equal(Path.GetFullPath(@"C:\out\drawing.png"), path);
        }

        [Fact]
        public void RootItself_IsAllowed()
        {
            Assert.Equal(Path.GetFullPath(@"C:\out"),
                PathPolicy.EnsureAllowed(@"C:\out", "path", Config(@"C:\out")));
        }

        [Fact]
        public void TraversalOutsideRoot_IsRejected()
        {
            var ex = Assert.Throws<ToolArgumentException>(() =>
                PathPolicy.EnsureAllowed(@"C:\out\..\Windows\x.txt", "path", Config(@"C:\out")));

            Assert.Contains("AllowedRoots", ex.Message);
        }

        [Fact]
        public void SiblingWithSameNamePrefix_IsRejected()
        {
            // "C:\out2" starts with "C:\out" as a string; only a separator boundary counts.
            Assert.Throws<ToolArgumentException>(() =>
                PathPolicy.EnsureAllowed(@"C:\out2\drawing.png", "path", Config(@"C:\out")));
        }

        [Fact]
        public void RootMatch_IsCaseInsensitive()
        {
            Assert.Equal(Path.GetFullPath(@"C:\OUT\drawing.png"),
                PathPolicy.EnsureAllowed(@"C:\OUT\drawing.png", "path", Config(@"c:\out")));
        }

        [Fact]
        public void SemicolonSeparatedRoots_AreAllHonoured()
        {
            var config = Config(@"C:\a;D:\b");

            Assert.Equal(Path.GetFullPath(@"D:\b\x.png"),
                PathPolicy.EnsureAllowed(@"D:\b\x.png", "path", config));
            Assert.Throws<ToolArgumentException>(() =>
                PathPolicy.EnsureAllowed(@"E:\c\x.png", "path", config));
        }

        [Fact]
        public void BlankPath_IsRejected()
        {
            Assert.Throws<ToolArgumentException>(() =>
                PathPolicy.EnsureAllowed("  ", "path", Config("")));
        }

        [Fact]
        public void Message_NamesTheArgumentAndTheRoots()
        {
            var ex = Assert.Throws<ToolArgumentException>(() =>
                PathPolicy.EnsureAllowed(@"D:\elsewhere\x.png", "targetPath", Config(@"C:\out")));

            Assert.Contains("targetPath", ex.Message);
            Assert.Contains(@"C:\out", ex.Message);
        }
    }
}
