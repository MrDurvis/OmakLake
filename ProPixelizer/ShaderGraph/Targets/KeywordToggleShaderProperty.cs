// Workaround for lack of [Toggle] in shader graph properties.

using System;
using UnityEditor.ShaderGraph.Drawing.Controls;

namespace UnityEditor.ShaderGraph.Internal
{
    [Serializable]
    [BlackboardInputInfo(20)]
    public sealed class KeywordToggleShaderProperty : AbstractShaderProperty<bool>
    {
        internal KeywordToggleShaderProperty()
        {
            displayName = "Boolean";
        }

        public override PropertyType propertyType => PropertyType.Boolean;

        internal override bool isExposable => true;
        internal override bool isRenamable => true;

        internal override string GetPropertyAsArgumentString(string precisionString)
        {
            return $"{concreteShaderValueType.ToShaderString(precisionString)} {referenceName}";
        }

        internal override void ForeachHLSLProperty(Action<HLSLProperty> action)
        {
            HLSLDeclaration decl = GetDefaultHLSLDeclaration();
            action(new HLSLProperty(HLSLType._float, referenceName, decl, concretePrecision));
        }

        internal override string GetPropertyBlockString()
        {
            return $"{hideTagString}[Toggle]{referenceName}(\"{displayName}\", Float) = {(value == true ? 1 : 0)}";
        }

        internal override AbstractMaterialNode ToConcreteNode()
        {
            return new BooleanNode { value = new ToggleData(value) };
        }

        internal override PreviewProperty GetPreviewMaterialProperty()
        {
            return new PreviewProperty(propertyType)
            {
                name = referenceName,
                booleanValue = value
            };
        }

        internal override ShaderInput Copy()
        {
            return new BooleanShaderProperty()
            {
                displayName = displayName,
                value = value,
            };
        }
    }
}
