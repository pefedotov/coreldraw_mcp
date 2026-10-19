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

        /// <summary>
        /// A dimension label is written in the unit the document uses. CorelDRAW's dimension
        /// enum is separate from <c>cdrUnit</c>, so every document unit that can hold a drawing
        /// must map to its dimension counterpart — a missing case silently labels an inch
        /// drawing in millimetres.
        /// </summary>
        [Theory]
        [InlineData(corel.cdrUnit.cdrMillimeter, corel.cdrDimensionLinearUnits.cdrDimensionUnitMillimeters)]
        [InlineData(corel.cdrUnit.cdrCentimeter, corel.cdrDimensionLinearUnits.cdrDimensionUnitCentimeters)]
        [InlineData(corel.cdrUnit.cdrMeter, corel.cdrDimensionLinearUnits.cdrDimensionUnitMeters)]
        [InlineData(corel.cdrUnit.cdrKilometer, corel.cdrDimensionLinearUnits.cdrDimensionUnitKilometers)]
        [InlineData(corel.cdrUnit.cdrInch, corel.cdrDimensionLinearUnits.cdrDimensionUnitInches)]
        [InlineData(corel.cdrUnit.cdrFoot, corel.cdrDimensionLinearUnits.cdrDimensionUnitFtX)]
        [InlineData(corel.cdrUnit.cdrYard, corel.cdrDimensionLinearUnits.cdrDimensionUnitYards)]
        [InlineData(corel.cdrUnit.cdrMile, corel.cdrDimensionLinearUnits.cdrDimensionUnitMiles)]
        [InlineData(corel.cdrUnit.cdrPoint, corel.cdrDimensionLinearUnits.cdrDimensionUnitPoints)]
        [InlineData(corel.cdrUnit.cdrPica, corel.cdrDimensionLinearUnits.cdrDimensionUnitPicas)]
        [InlineData(corel.cdrUnit.cdrCicero, corel.cdrDimensionLinearUnits.cdrDimensionUnitCiceros)]
        public void DocumentUnitMapsToTheMatchingDimensionUnit(corel.cdrUnit document,
            corel.cdrDimensionLinearUnits expected)
        {
            Assert.Equal(expected, Units.DimensionUnits(document));
        }

        [Fact]
        public void UnknownDocumentUnitFallsBackToMillimetres()
        {
            Assert.Equal(corel.cdrDimensionLinearUnits.cdrDimensionUnitMillimeters,
                Units.DimensionUnits((corel.cdrUnit)12345));
        }
    }
}
