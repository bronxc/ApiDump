using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Colorful;
using Microsoft.Cci;
using Microsoft.Cci.Extensions;
using Microsoft.Cci.Extensions.CSharp;
using Console = Colorful.Console;

namespace ApiDump
{
    internal class AssemblyPrinter
    {
        private readonly IAssembly _assembly;
        private readonly StyleSheet _memberStyle;

        private AssemblyPrinter(IAssembly assembly)
        {
            _assembly = assembly;

            _memberStyle = new StyleSheet(Color.White);
            _memberStyle.AddStyle("^[ ]{4,4}[ !@][CMPFX][AOV]? ", Color.BlueViolet); // Type identifier
            _memberStyle.AddStyle(@"\([a-zA-Z\.<>0-9\[\]]+\)", Color.DodgerBlue); // Return/Field type
            _memberStyle.AddStyle(@"\[[a-zA-Z\.<>]+\]", Color.SlateGray); // CCI Type
            _memberStyle.AddStyle(@"<[a-zA-Z,0-9]+>", Color.Coral); // Generic Parameters
            _memberStyle.AddStyle(@"(R | W|RW) ", Color.GreenYellow); // Property R/W
            _memberStyle.AddStyle(@"RO ", Color.DarkCyan); // Field readonly
        }

        public static void Dump(string assemblyPath)
        {
            var assembly = HostEnvironment.LoadAssemblySet(assemblyPath).FirstOrDefault();

            if (assembly == null)
                throw new Exception("assembly is null??");

            new AssemblyPrinter(assembly).Print();
        }

        private void Print()
        {
            foreach (INamedTypeDefinition typeDefinition in _assembly.GetAllTypes())
            {
                if (!typeDefinition.IsVisibleOutsideAssembly())
                {
                    Console.WriteLine($"- {typeDefinition} is not visible outside of assembly", Color.SlateGray);
                    continue;
                }

                PrintType(typeDefinition);
            }
        }

        private void PrintType(INamedTypeDefinition typeDefinition)
        {
            // Type Modifiers
            string typeModifer = " ";
            if (typeDefinition.IsStatic)
                typeModifer = "!";
            else if (typeDefinition.IsSealed)
                typeModifer = "S";
            else if (!typeDefinition.IsInterface && typeDefinition.IsAbstract)
                typeModifer = "A";

            Console.Write($"{typeModifer} ", Color.BlueViolet);

            // Type Name
            if (typeDefinition.IsInterface)
                Console.Write(typeDefinition, Color.GreenYellow);
            else if (typeDefinition.IsEnum)
                Console.Write(typeDefinition, Color.LightSalmon);
            else
                Console.Write(typeDefinition, Color.White);

            // Generic Arguments
            if (typeDefinition.IsGeneric)
            {
                var genericArgs = string.Join(",", typeDefinition.GenericParameters
                    .Select(p => p.Name.Value));

                Console.Write($"<{genericArgs}>", Color.Coral);
            }

            // Underlying CCI Type
            Console.WriteLine($" [{typeDefinition.GetType().Name}]", Color.SlateGray);

            // Member Printers

            // Enum Printing
            if (typeDefinition.IsEnum)
            {
                PrintEnumMemebers(typeDefinition);
                return;
            }

            if (typeDefinition.IsGeneric)
                PrintGenericConstraints("    ", typeDefinition.GenericParameters);
            
            PrintTypeMembers(typeDefinition);
        }

        private void PrintTypeMembers(INamedTypeDefinition typeDefinition)
        {
            foreach (ITypeDefinitionMember member in typeDefinition.Members)
            {
                if (!member.IsVisibleOutsideAssembly())
                    continue;

                switch (member)
                {
                    case IMethodDefinition methodDefinition:
                        PrintMethodDefinition(methodDefinition);
                        break;

                    case IPropertyDefinition propertyDefinition:
                        PrintPropertyDefinition(propertyDefinition);
                        break;

                    case IFieldDefinition fieldDefinition:
                        PrintFieldDefinition(fieldDefinition);
                        break;

                    default:
                        Console.WriteLine($"\t* {member.Name.Value} ({member.GetType()})");
                        break;
                }
            }
        }

        private void PrintFieldDefinition(IFieldDefinition fieldDefinition)
        {
            string staticIdentifier = fieldDefinition.IsStatic ? fieldDefinition.IsCompileTimeConstant ? "@" : "!" : " ";
            string roFlag = fieldDefinition.IsReadOnly ? "RO" : string.Empty;

            Console.WriteLineStyled($"    {staticIdentifier}F {fieldDefinition.Name.Value} {roFlag} ({fieldDefinition.GetReturnType()}) [{fieldDefinition.GetType().Name}]", _memberStyle);
        }

        private void PrintPropertyDefinition(IPropertyDefinition propertyDefinition)
        {
            string staticIdentifier = GetStaticIdentifier(propertyDefinition);
            string memberOverride = GetMemberOverride(propertyDefinition);

            string modifyAttributes = string.Empty;
            modifyAttributes += propertyDefinition.Getter != null && propertyDefinition.Getter.ResolvedMethod.IsVisibleOutsideAssembly() ? "R" : " ";
            modifyAttributes += propertyDefinition.Setter != null && propertyDefinition.Setter.ResolvedMethod.IsVisibleOutsideAssembly() ? "W" : " ";

            Console.WriteLineStyled($"    {staticIdentifier}P{memberOverride} {propertyDefinition.Name.Value} {modifyAttributes} ({propertyDefinition.GetReturnType()}) [{propertyDefinition.GetType().Name}]", _memberStyle);
        }

        private void PrintMethodDefinition(IMethodDefinition methodDefinition)
        {
            const string parameterPrefix = "\t  ";

            // Method Prefixes
            string staticIdentifier = GetStaticIdentifier(methodDefinition);
            string methodCode = GetMethodCode(methodDefinition);
            string memberOverride = GetMemberOverride(methodDefinition);

            // Generic Arguments
            string genericArgs = string.Empty;
            if (methodDefinition.IsGeneric)
            {
                genericArgs = string.Join(",", methodDefinition.GenericParameters
                    .Select(p => p.Name.Value));
                genericArgs = $"<{genericArgs}>";
            }

            Console.WriteLineStyled($"    {staticIdentifier}{methodCode}{memberOverride} {methodDefinition.Name.Value}{genericArgs} ({methodDefinition.GetReturnType()}) [{methodDefinition.GetType().Name}]", _memberStyle);

            if (methodDefinition.IsGeneric)
            {
                PrintGenericConstraints(parameterPrefix, methodDefinition.GenericParameters);
            }

            foreach (IParameterDefinition parameter in methodDefinition.Parameters)
            {
                Console.WriteLineStyled($"{parameterPrefix}{parameter.Name} ({parameter.Type})", _memberStyle);
            }
        }

        private static string GetStaticIdentifier(ISignature methodDefinition)
        {
            return methodDefinition.IsStatic ? "!" : " ";
        }

        private void PrintGenericConstraints(string prefix, IEnumerable<IGenericParameter> methodDefinition)
        {
            foreach (IGenericParameter genericParameter in methodDefinition)
            {
                var constraintStrings = new List<string>();

                foreach (ITypeReference constraint in genericParameter.Constraints)
                {
                    if (constraint is Dummy)
                        continue;

                    constraintStrings.Add(constraint.FullName());
                }

                if (genericParameter.MustHaveDefaultConstructor)
                    constraintStrings.Add("new()");

                if (genericParameter.MustBeReferenceType)
                    constraintStrings.Add("class");

                if (genericParameter.MustBeValueType)
                    constraintStrings.Add("struct");

                // Don't print anything if there are no constraints
                if (constraintStrings.Count == 0)
                    continue;

                Console.Write(prefix);
                Console.Write(genericParameter.Name.Value, Color.Coral);
                Console.Write(" : ", Color.White);
                Console.WriteLine(string.Join(", ", constraintStrings), Color.DodgerBlue);
            }
        }

        private void PrintEnumMemebers(INamedTypeDefinition typeDefinition)
        {
            foreach (IFieldDefinition fieldDefinition in typeDefinition.Fields)
            {
                if (!fieldDefinition.IsStatic)
                    continue;

                Console.Write($"    {fieldDefinition.Name.Value} - {fieldDefinition.CompileTimeValue.Value}", Color.White);
                Console.WriteLine($"  [{fieldDefinition.GetType().Name}]", Color.SlateGray);
            }
        }

        private static string GetMethodCode(IMethodDefinition methodDefinition)
        {
            if (methodDefinition.IsConstructor)
                return "C";

            // We should probably filter these out at some point
            if (methodDefinition.IsPropertyOrEventAccessor())
                return "X";

            // This is covered by GetMemberOverride
            /*if (methodDefinition.IsAbstract && !methodDefinition.Container.IsInterface)
                return "A";*/

            return "M";
        }

        private static string GetMemberOverride(ITypeDefinitionMember memberDefinition)
        {
            // Captain Obvious: Interface members are abstract/virtual.
            if (memberDefinition.ContainingTypeDefinition.IsInterface)
                return " ";

            if (memberDefinition.IsOverride())
                return "O";
            
            if (memberDefinition.IsAbstract())
                return "A";
            
            if (memberDefinition.IsVirtual())
                return "V";

            return " ";
        }
    }
}