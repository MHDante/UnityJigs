using System;
using System.Runtime.CompilerServices;
using JetBrains.Annotations;

namespace UnityJigs.Attributes
{
    // What a design enum says a designer may do to its values from the Enums window (Utils > Enums), without a
    // programmer. An enum declares these on its [DesignEnum]; the window offers only what is declared.
    //
    //   Readable    - an animator (a parameter, or a field set on an animator state) uses its values, so a designer
    //                 needs to see each value's number.
    //   Addable     - a new value does something on its own: picking it in an inspector or an animator is enough.
    //   Removable   - Addable, and no code names any of its values, so deleting one breaks no code.
    //   Reorderable - Readable or Addable, and no code cares about order (no comparisons, ranges or arrays by value).
    //                 Values keep their numbers when moved, so only the order in the file and in dropdowns changes.
    // An enum that is neither Readable nor Addable is not a design enum.
    //
    // Design enums give every value an explicit number: saved scenes, assets and animators hold the numbers, so a
    // value must keep its number for as long as it exists.
    [Flags]
    public enum DesignEnumTraits
    {
        None = 0,
        Readable = 1,
        Addable = 2,
        Removable = 4,
        Reorderable = 8,
    }

    [MeansImplicitUse(ImplicitUseTargetFlags.WithMembers)]
    [AttributeUsage(AttributeTargets.Enum)]
    public class DesignEnumAttribute : Attribute
    {
        public DesignEnumTraits Traits { get; }
        public string File { get; }
        public int Line { get; }

        public DesignEnumAttribute(DesignEnumTraits traits, [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
        {
            Traits = traits;
            File = file;
            Line = line;
        }

        // The old form declares nothing, and nothing is what it allows: the window shows such an enum read only.
        [Obsolete("Say what the enum allows, e.g. [DesignEnum(DesignEnumTraits.Readable | DesignEnumTraits.Reorderable)].")]
        public DesignEnumAttribute([CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
            : this(DesignEnumTraits.None, file, line) { }

        public bool Allows(DesignEnumTraits trait) => (Traits & trait) == trait;

        // Why the declaration breaks the rules, or null when it keeps them.
        public string? Problem
        {
            get
            {
                if (!Allows(DesignEnumTraits.Readable) && !Allows(DesignEnumTraits.Addable))
                    return "It says neither Readable nor Addable, so it isn't a design enum.";
                if (Allows(DesignEnumTraits.Removable) && !Allows(DesignEnumTraits.Addable))
                    return "It says Removable without Addable.";
                return null;
            }
        }
    }
}
