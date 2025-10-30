namespace UnityJigs.Fmod.Editor
{
    public struct FmodParameterMeta
    {
        public string   Name;
        public float    Min;
        public float    Max;
        public float    Default;
        public bool     HasRange;
        public bool     IsLabeled;
        public string[]? Labels;
    }
}
