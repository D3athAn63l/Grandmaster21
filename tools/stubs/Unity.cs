using System;
namespace UnityEngine
{
    public struct Rect
    {
        public float x, y, width, height;
        public Rect(float x, float y, float w, float h) { this.x=x; this.y=y; this.width=w; this.height=h; }
        public float xMax { get { return x + width; } }
        public float yMax { get { return y + height; } }
    }
    public struct Color
    {
        public float r,g,b,a;
        public Color(float r,float g,float b) { this.r=r;this.g=g;this.b=b;this.a=1f; }
        public Color(float r,float g,float b,float a) { this.r=r;this.g=g;this.b=b;this.a=a; }
        public static Color white { get { return new Color(1,1,1); } }
        public static Color yellow { get { return new Color(1,1,0); } }
    }
    public enum TextAnchor { UpperLeft, UpperCenter, UpperRight, MiddleLeft, MiddleCenter, MiddleRight, LowerLeft, LowerCenter, LowerRight }
    public static class Mathf
    {
        public static int Clamp(int v,int a,int b){ return v<a?a:(v>b?b:v); }
        public static float Clamp(float v,float a,float b){ return v<a?a:(v>b?b:v); }
        public static float Max(float a,float b){ return a>b?a:b; }
        public static float Min(float a,float b){ return a<b?a:b; }
        public static int Max(int a,int b){ return a>b?a:b; }
        public static int Min(int a,int b){ return a<b?a:b; }
        public static int RoundToInt(float f){ return (int)System.Math.Round((double)f); }
    }
    public static class GUI { public static Color color; }
    public class Texture2D { }
}
