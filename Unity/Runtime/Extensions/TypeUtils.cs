using System;
using System.Text;
using Sirenix.Utilities;

namespace UnityJigs.Extensions
{
    public static class TypeUtils
    {
        public static string GetFullCompilerSafeTypeName(this Type type)
        {
            var sb = new StringBuilder();
            sb.AppendFullTypeName(type);
            return sb.ToString();
        }

        public static StringBuilder AppendFullTypeName(this StringBuilder sb, Type type)
        {
            if (type == null)
                throw new ArgumentNullException(nameof(type));

            if (!type.IsGenericType)
            {
                if (TypeExtensions.TypeNameAlternatives.TryGetValue(type, out var s)) return sb.Append(s);
                return sb.Append(type.FullName);
            }

            var genericDefinition = type.GetGenericTypeDefinition();

            // Write the namespace and basic type name, skipping the generic parameters
            sb.Append(genericDefinition.Namespace);
            sb.Append(".");
            sb.Append(genericDefinition.Name[..genericDefinition.Name.IndexOf('`')]);
            // Deal with the generic type arguments
            sb.Append("<");
            var genericArguments = type.GetGenericArguments();
            for (int i = 0; i < genericArguments.Length; i++)
            {
                if (i > 0)
                    sb.Append(", ");
                sb.AppendFullTypeName(genericArguments[i]);
            }
            return sb.Append(">");

        }
    }
}
