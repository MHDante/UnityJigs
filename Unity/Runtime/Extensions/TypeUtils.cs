using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;

namespace UnityJigs.Extensions
{
    public static class TypeUtils
    {
        /// <summary>Type name alias lookup.</summary>
        public static readonly Dictionary<Type, string> TypeNameAlternatives = new()
        {
            { typeof(float), "float" },
            { typeof(double), "double" },
            { typeof(sbyte), "sbyte" },
            { typeof(short), "short" },
            { typeof(int), "int" },
            { typeof(long), "long" },
            { typeof(byte), "byte" },
            { typeof(ushort), "ushort" },
            { typeof(uint), "uint" },
            { typeof(ulong), "ulong" },
            { typeof(decimal), "decimal" },
            { typeof(string), "string" },
            { typeof(char), "char" },
            { typeof(bool), "bool" },
            { typeof(float[]), "float[]" },
            { typeof(double[]), "double[]" },
            { typeof(sbyte[]), "sbyte[]" },
            { typeof(short[]), "short[]" },
            { typeof(int[]), "int[]" },
            { typeof(long[]), "long[]" },
            { typeof(byte[]), "byte[]" },
            { typeof(ushort[]), "ushort[]" },
            { typeof(uint[]), "uint[]" },
            { typeof(ulong[]), "ulong[]" },
            { typeof(decimal[]), "decimal[]" },
            { typeof(string[]), "string[]" },
            { typeof(char[]), "char[]" },
            { typeof(bool[]), "bool[]" }
        };

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
                if (TypeNameAlternatives.TryGetValue(type, out var s)) return sb.Append(s);
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

        /// <summary>
        /// Returns the first found custom attribute of type T on this member
        /// Returns null if none was found
        /// </summary>
        public static T GetAttribute<T>(this ICustomAttributeProvider member, bool inherit) where T : Attribute
        {
            T[] array = member.GetAttributes<T>(inherit).ToArray<T>();
            return array != null && array.Length != 0 ? array[0] : default (T);
        }

        /// <summary>
        /// Returns the first found non-inherited custom attribute of type T on this member
        /// Returns null if none was found
        /// </summary>
        public static T GetAttribute<T>(this ICustomAttributeProvider member) where T : Attribute
        {
            return member.GetAttribute<T>(false);
        }

        /// <summary>Gets all attributes of the specified generic type.</summary>
        /// <param name="member">The member.</param>
        public static IEnumerable<T> GetAttributes<T>(this ICustomAttributeProvider member) where T : Attribute
        {
            return member.GetAttributes<T>(false);
        }

        /// <summary>Gets all attributes of the specified generic type.</summary>
        /// <param name="member">The member.</param>
        /// <param name="inherit">If true, specifies to also search the ancestors of element for custom attributes.</param>
        public static IEnumerable<T> GetAttributes<T>(
            this ICustomAttributeProvider member,
            bool inherit)
            where T : Attribute
        {
            try
            {
                return Enumerable.Cast<T>(member.GetCustomAttributes(typeof (T), inherit));
            }
            catch
            {
                return (IEnumerable<T>) new T[0];
            }
        }

    }
}
