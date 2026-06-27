using System;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MegaCallstack.Services;

namespace MegaCallstack.Tests
{
    [TestClass]
    public class SolutionRootDetectorTests
    {
        /// <summary>
        /// Normalizes a path to the form the detector returns (OS separators,
        /// no trailing separator) so tests are comparable across platforms.
        /// </summary>
        private static string Norm(string path)
        {
            return path.Replace('/', Path.DirectorySeparatorChar);
        }

        [TestMethod]
        public void Detect_NullInput_ReturnsEmpty()
        {
            var result = SolutionRootDetector.DetectProjectFolders(null, 5);
            Assert.AreEqual(0, result.Count);
        }

        [TestMethod]
        public void Detect_EmptyInput_ReturnsEmpty()
        {
            var result = SolutionRootDetector.DetectProjectFolders(new string[0], 5);
            Assert.AreEqual(0, result.Count);
        }

        [TestMethod]
        public void Detect_ZeroMax_ReturnsEmpty()
        {
            var result = SolutionRootDetector.DetectProjectFolders(
                new[] { "C:/Proj/Program.cs" }, 0);
            Assert.AreEqual(0, result.Count);
        }

        [TestMethod]
        public void Detect_SingleFlatProject_ReturnsProjectDirectory()
        {
            var files = new[]
            {
                "C:/Proj/Program.cs",
                "C:/Proj/Worker.cs",
                "C:/Proj/Helper.cs"
            };

            var result = SolutionRootDetector.DetectProjectFolders(files, 5);

            Assert.AreEqual(1, result.Count);
            Assert.AreEqual(Norm("C:/Proj"), result[0]);
        }

        [TestMethod]
        public void Detect_TwoProjectsOnDifferentDrives_ReturnsBothRoots()
        {
            var files = new[]
            {
                "D:/App/Program.cs",
                "D:/App/Form.cs",
                "D:/App/Util.cs",
                "E:/Lib/Helper.cs",
                "E:/Lib/Math.cs",
                "E:/Lib/IO.cs"
            };

            var result = SolutionRootDetector.DetectProjectFolders(files, 5);

            Assert.AreEqual(2, result.Count);
            CollectionAssert.AreEquivalent(
                new[] { Norm("D:/App"), Norm("E:/Lib") },
                result.ToList());
        }

        [TestMethod]
        public void Detect_SiblingProjectsUnderCommonFolder_ReturnsCommonParent()
        {
            // When two projects live under a shared ancestor, the algorithm
            // naturally promotes that ancestor (it is a branch point) rather
            // than returning both children. This is the desired behavior: the
            // common folder is a valid single root covering both projects.
            var files = new[]
            {
                "C:/Code/App/Program.cs",
                "C:/Code/App/Form.cs",
                "C:/Code/App/Util.cs",
                "C:/Code/Lib/Helper.cs",
                "C:/Code/Lib/Math.cs",
                "C:/Code/Lib/IO.cs"
            };

            var result = SolutionRootDetector.DetectProjectFolders(files, 5);

            Assert.AreEqual(1, result.Count);
            Assert.AreEqual(Norm("C:/Code"), result[0]);
        }

        [TestMethod]
        public void Detect_KCap_LimitsResultCount()
        {
            var files = new[]
            {
                "D:/App/Program.cs",
                "D:/App/Form.cs",
                "D:/App/Util.cs",
                "E:/Lib/Helper.cs",
                "E:/Lib/Math.cs",
                "E:/Lib/IO.cs"
            };

            var result = SolutionRootDetector.DetectProjectFolders(files, 1);

            Assert.AreEqual(1, result.Count);
        }

        [TestMethod]
        public void Detect_NoParentChildRedundancyInResults()
        {
            // A parent and its child can both be branch points, but the greedy
            // step must never return both: the child is redundant once the
            // parent (or vice versa) is selected.
            var files = new[]
            {
                "D:/App/Program.cs",
                "D:/App/Form.cs",
                "D:/App/Util.cs",
                "D:/App/Sub/More.cs",
                "D:/App/Sub/Extra.cs",
                "D:/App/Sub/Other.cs"
            };

            var result = SolutionRootDetector.DetectProjectFolders(files, 5);

            foreach (var root in result)
            {
                foreach (var other in result)
                {
                    if (ReferenceEquals(root, other))
                        continue;
                    // Neither root may be a prefix (parent) of the other.
                    Assert.IsFalse(
                        other.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase),
                        $"Redundant nested roots returned: '{root}' contains '{other}'");
                }
            }
        }

        [TestMethod]
        public void Detect_Loosening_FillsUpToKWithNestedCandidates()
        {
            // Two branch points where one is the parent of the other. Step 3
            // selects only the parent (child is redundant), leaving the result
            // short of K. Step 4 (loosening) then admits the nested child to
            // fill the remainder.
            var files = new[]
            {
                "D:/App/Program.cs",
                "D:/App/Form.cs",
                "D:/App/Util.cs",
                "D:/App/Sub/More.cs",
                "D:/App/Sub/Extra.cs",
                "D:/App/Sub/Other.cs"
            };

            var result = SolutionRootDetector.DetectProjectFolders(files, 5);

            // Parent is always selected; the nested child is admitted by the
            // loosening step to reach 2.
            Assert.AreEqual(2, result.Count);
            CollectionAssert.AreEquivalent(
                new[] { Norm("D:/App"), Norm("D:/App/Sub") },
                result.ToList());
        }

        [TestMethod]
        public void Detect_DriveRootNeverReturned()
        {
            // Even when all files share only a drive root as common ancestor,
            // the bare drive (depth 1) is never returned as a project root.
            var files = new[]
            {
                "C:/ProjA/a.cs",
                "C:/ProjA/b.cs",
                "C:/ProjB/c.cs",
                "C:/ProjB/d.cs"
            };

            var result = SolutionRootDetector.DetectProjectFolders(files, 5);

            foreach (var root in result)
            {
                // A bare drive root would be <= 3 chars (e.g. "C:\").
                Assert.IsTrue(
                    root.Length > 3,
                    $"Bare drive root returned as a project root: '{root}'");
            }
        }

        [TestMethod]
        public void Detect_BackslashPathsHandledLikeForwardSlash()
        {
            // EnvDTE on Windows returns backslash paths; the detector must
            // treat them identically to forward-slash input.
            var files = new[]
            {
                "C:\\Proj\\Program.cs",
                "C:\\Proj\\Worker.cs",
                "C:\\Proj\\Helper.cs"
            };

            var result = SolutionRootDetector.DetectProjectFolders(files, 5);

            Assert.AreEqual(1, result.Count);
            Assert.AreEqual(Norm("C:/Proj"), result[0]);
        }

        [TestMethod]
        public void Detect_ResultPathsUseOsSeparators()
        {
            var files = new[]
            {
                "C:/Proj/Program.cs",
                "C:/Proj/Worker.cs"
            };

            var result = SolutionRootDetector.DetectProjectFolders(files, 5);

            Assert.AreEqual(1, result.Count);
            // Output must use the OS separator, not forward slashes.
            Assert.IsFalse(result[0].Contains('/'));
        }
    }
}
