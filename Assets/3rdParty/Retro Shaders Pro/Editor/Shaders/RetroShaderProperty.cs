using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.Rendering;

namespace RetroShadersPro.URP.Editor
{
    public struct RetroShaderProperty
    {
        public MaterialProperty prop;
        public readonly string name;
        public readonly GUIContent info;
        public readonly int id;

        public RetroShaderProperty(string name)
        {
            prop = null;
            this.name = name;
            info = null;
            id = Shader.PropertyToID(name);
        }

        public RetroShaderProperty(string name, GUIContent info)
        {
            prop = null;
            this.name = name;
            this.info = info;
            id = Shader.PropertyToID(name);
        }

        public RetroShaderProperty(string name, string prettyName, string description) :
            this(name, new GUIContent(prettyName, description))
        { }
    }
}
