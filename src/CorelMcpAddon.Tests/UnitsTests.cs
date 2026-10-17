using Xunit;
using CorelMcp.Tools;
using corel = Corel.Interop.VGCore;

namespace CorelMcpAddon.Tests
{
    /// <summary>
    /// Unit conversion guards the export path: export_png derives the pixel size from the
    /// page size, and reading the page in the wrong unit was what produced a 6000 dpi image.
    /// </summary>
    public class UnitsTests
    {
        [Fact]
        public void MillimetresPassThrough()
        {
            Assert.Equal(210.0, Units.ToMillimeters(210.0, corel.cdrUnit.cdrMillimeter));
        }

        [Fact]
        public void CommonUnitsConvertCorrectly()
        {
            Assert.Equal(10.0, Units.ToMillimeters(1.0, corel.cdrUnit.cdrCentimeter), 6);
            Assert.Equal(25.4, Units.ToMillimeters(1.0, corel.cdrUnit.cdrInch), 6);
            Assert.Equal(304.8, Units.ToMillimeters(1.0, corel.cdrUnit.cdrFoot), 6);
            Assert.Equal(1000.0, Units.ToMillimeters(1.0, corel.cdrUnit.cdrMeter), 6);
        }

        [Fact]
        public void A4PageGivesSanePixelsAtTypicalResolutions()
        {
            // A4 in inches is 8.27 x 11.69
            double widthMm = Units.ToMillimeters(8.2677, corel.cdrUnit.cdrInch);
            double heightMm = Units.ToMillimeters(11.6929, corel.cdrUnit.cdrInch);

            Assert.Equal(1240, (int)System.Math.Round(widthMm / 25.4 * 150));
            Assert.Equal(1754, (int)System.Math.Round(heightMm / 25.4 * 150));
        }
    }
}
