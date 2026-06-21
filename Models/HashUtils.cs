namespace MegaCallstack.Models
{
    public static class HashUtils
    {
        public static int FNV1a(int hash, string value)
        {
            unchecked
            {
                if (!string.IsNullOrEmpty(value))
                {
                    for (int i = 0; i < value.Length; i++)
                    {
                        hash ^= value[i];
                        hash *= 16777619;
                    }
                }
                return hash;
            }
        }
    }
}
