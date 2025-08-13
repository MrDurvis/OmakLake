// Copyright Elliot Bentine, 2018-
namespace ProPixelizer.Universal.ShaderGraph
{
    public static class ProPixelizerURPConstants
    {
        /// <summary>
        /// Link to the ProPixelizer User Guide
        /// </summary>
        public static readonly string USER_GUIDE_URL = "https://propixelizer.github.io/docs";

        public static readonly string SHADERGRAPH_USER_GUIDE_URL = "https://propixelizer.github.io/docs/usage/shadergraph/";
    }

    public static class ProPixelizerMaterialPropertyReferences
    {
        public static readonly string OutlineColor = "_OutlineColor";
        public static readonly string EdgeHighlightColor = "_EdgeHighlightColor";
        public static readonly string PixelSize = "_PixelSize";
        public static readonly string AmbientLight = "_AmbientLight";
        public static readonly string PixelGridOrigin = "_PixelGridOrigin";
        public static readonly string PaletteLUT = "_PaletteLUT";
        public static readonly string LightingRamp = "_LightingRamp";
        public static readonly string ID = "_ID";
        public static readonly string COLOR_GRADING = "COLOR_GRADING";
    }

    /// <summary>
    /// Names for material properties on the ProPixelizer SubTarget
    /// </summary>
    public static class ProPixelizerMaterialPropertyLabels
    {
        public static readonly string PixelSize = "PixelSize";
        public static readonly string PaletteLUT = "PaletteLUT";
        public static readonly string LightingRamp = "LightingRamp";
        public static readonly string ID = "ID";
        public static readonly string UseColorGrading = "Use Color Grading";
        public static readonly string UseDithering = "Use Dithering";
        public static readonly string PixelGridOrigin = "PixelGridOrigin";
        public static readonly string AmbientLight = "AmbientLight";
        public static readonly string COLOR_GRADING = "COLOR_GRADING";
    }

    /// <summary>
    /// Keyword strings used by the ProPixelizer SubTarget
    /// </summary>
    public static class ProPixelizerMaterialKeywordStrings
    {
        public static readonly string COLOR_GRADING = "COLOR_GRADING";
        public static readonly string COLOR_GRADING_ON = "COLOR_GRADING_ON";
        public static readonly string PROPIXELIZER_DITHERING_ON = "PROPIXELIZER_DITHERING_ON";
        public static readonly string PROPIXELIZER_DITHERING = "PROPIXELIZER_DITHERING";
        public static readonly string USE_OBJECT_POSITION_FOR_PIXEL_GRID_ON = "USE_OBJECT_POSITION_FOR_PIXEL_GRID_ON";
    }
}
