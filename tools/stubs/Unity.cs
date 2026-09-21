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
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x=x; this.y=y; this.z=z; }
        public static Vector3 zero { get { return new Vector3(0,0,0); } }
        public static Vector3 operator -(Vector3 a, Vector3 b) { return new Vector3(a.x-b.x, a.y-b.y, a.z-b.z); }
        public static Vector3 operator +(Vector3 a, Vector3 b) { return new Vector3(a.x+b.x, a.y+b.y, a.z+b.z); }
        public float magnitude { get { return Mathf.Sqrt(x*x + y*y + z*z); } }
    }
    public struct Quaternion { public static Quaternion identity { get { return default(Quaternion); } } }

    public static class Mathf
    {
        public static float Sqrt(float f){ return (float)System.Math.Sqrt(f); }
        public static float Cos(float f){ return (float)System.Math.Cos(f); }
        public static float Sin(float f){ return (float)System.Math.Sin(f); }
        public static float Abs(float f){ return f<0?-f:f; }
        public static float Pow(float a,float b){ return (float)System.Math.Pow(a,b); }
        public static int FloorToInt(float f){ return (int)System.Math.Floor((double)f); }
        public static int CeilToInt(float f){ return (int)System.Math.Ceiling((double)f); }
        public const float PI = 3.14159265358979f;
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
