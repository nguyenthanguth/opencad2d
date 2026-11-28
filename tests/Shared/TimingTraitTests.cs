using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using Xunit;

namespace OpenCAD2D
{
    /// <summary>
    /// Every test in this assembly that asserts elapsed time (<c>SpeedGuard</c> or <c>Stopwatch</c>) carries
    /// <c>[Trait("Category", "Timing")]</c> so a hosted runner can filter <c>Category!=Timing</c>, and its class sits in
    /// <see cref="ViewerSpeedCollection"/> so it runs alone after the parallel suites.
    /// </summary>
    public class TimingTraitTests
    {
        private static readonly OpCode[] OneByteOpCodes = BuildOpCodes(false);
        private static readonly OpCode[] TwoByteOpCodes = BuildOpCodes(true);

        // Tests that read a Stopwatch only to print diagnostics or to bound a wait loop, without asserting how fast the work ran.
        private static readonly HashSet<string> NotTimingAssertions = new HashSet<string>(StringComparer.Ordinal)
        {
            "LispGrReadTests.AThousandMoves_RunTheLoopOnceEach_WellUnderASecond",
            "SelectionCommandTests.FindAndQuickSelect_OverAHundredThousandObjects_AreTimed",
            "SnapshotResetSpeedTests.AThousandEpisodesOfAHundredEdits_EachEndedByAReset_LeaveTheMemoryWhereItWas",
            "SourcePluginCompilerTests.ASecondCompileOfUnchangedFiles_IsTakenFromTheCache_WithTheWarningsStillGiven",
            "PythonToolSnapProcessTests.AToolWhosePackagesAreNotApproved_IsNotStartedByASnap_AndNothingIsAsked",
            "ToolHostTests.NoProcessIsLeft_AfterTheHostIsDisposed_ForTheJobEndsThemAll",
            "ToolReadyLineTests.TheReadyLineOfATool_IsReadWhileTheWindowsThreadIsBusy",
        };

        private static readonly HashSet<string> PollingHelpers = new HashSet<string>(StringComparer.Ordinal)
        {
            "WaitUntil",
            "Pump",
            "WatchForText",
        };

        [Fact]
        public void EveryTimingTest_CarriesTheTimingTrait_AndSitsInTheSpeedCollection()
        {
            var definition = typeof(ViewerSpeedCollection).GetCustomAttribute<CollectionDefinitionAttribute>();
            Assert.NotNull(definition);
            Assert.True(definition.DisableParallelization);

            var violations = new List<string>();
            Assembly assembly = typeof(ViewerSpeedCollection).Assembly;

            foreach (Type type in assembly.GetTypes().OrderBy(t => t.FullName, StringComparer.Ordinal))
            {
                if (type == typeof(TimingTraitTests) || type.Name == "SpeedGuardMessageTests")
                {
                    continue;
                }

                MethodInfo[] tests = type.GetMethods(
                        BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly
                    )
                    .Where(m => m.IsDefined(typeof(FactAttribute), true))
                    .OrderBy(m => m.Name, StringComparer.Ordinal)
                    .ToArray();

                if (tests.Length == 0)
                {
                    continue;
                }

                bool inSpeedCollection = InSpeedCollection(type);

                foreach (MethodInfo method in tests)
                {
                    bool times = TimesWork(type, method);
                    bool tagged = HasTimingTrait(type, method);

                    if (times && !tagged)
                    {
                        violations.Add(type.Name + "." + method.Name + ": missing [Trait(\"Category\", \"Timing\")]");
                    }

                    if ((times || tagged) && !inSpeedCollection)
                    {
                        violations.Add(
                            type.Name + "." + method.Name + ": class is not in [Collection(ViewerSpeedCollection.Name)]"
                        );
                    }
                }
            }

            Assert.Empty(violations);
        }

        private static bool HasTimingTrait(Type type, MethodInfo method) =>
            HasTimingTraitOn(method) || HasTimingTraitOn(type);

        private static bool HasTimingTraitOn(MemberInfo member) =>
            CustomAttributeData
                .GetCustomAttributes(member)
                .Any(a =>
                    a.AttributeType == typeof(TraitAttribute)
                    && a.ConstructorArguments.Count == 2
                    && (string)a.ConstructorArguments[0].Value == "Category"
                    && (string)a.ConstructorArguments[1].Value == "Timing"
                );

        private static bool InSpeedCollection(Type type) =>
            CustomAttributeData
                .GetCustomAttributes(type)
                .Any(a =>
                    a.AttributeType == typeof(CollectionAttribute)
                    && a.ConstructorArguments.Count == 1
                    && (string)a.ConstructorArguments[0].Value == ViewerSpeedCollection.Name
                );

        private static bool TimesWork(Type testType, MethodInfo testMethod)
        {
            if (NotTimingAssertions.Contains(testType.Name + "." + testMethod.Name))
            {
                return false;
            }

            var visited = new HashSet<MethodBase>();
            var queue = new Queue<MethodBase>();
            Enqueue(testMethod, visited, queue);

            while (queue.Count > 0)
            {
                MethodBase current = queue.Dequeue();

                foreach (MethodBase called in CalledMethods(current))
                {
                    Type declaring = called.DeclaringType;

                    if (declaring == null)
                    {
                        continue;
                    }

                    if (declaring.Name == "SpeedGuard" && (called.Name == "AssertWithin" || called.Name == "Describe"))
                    {
                        return true;
                    }

                    if (
                        declaring == typeof(Stopwatch)
                        && (
                            called.Name == "get_Elapsed"
                            || called.Name == "get_ElapsedMilliseconds"
                            || called.Name == "get_ElapsedTicks"
                        )
                    )
                    {
                        return true;
                    }

                    if (
                        BelongsTo(declaring, testType)
                        && !PollingHelpers.Contains(called.Name)
                        && !called.IsDefined(typeof(FactAttribute), true)
                    )
                    {
                        Enqueue(called, visited, queue);
                    }
                }
            }

            return false;
        }

        private static void Enqueue(MethodBase method, HashSet<MethodBase> visited, Queue<MethodBase> queue)
        {
            if (!visited.Add(method))
            {
                return;
            }

            queue.Enqueue(method);

            if (method is MethodInfo info)
            {
                var asyncMachine = info.GetCustomAttribute<AsyncStateMachineAttribute>();

                if (asyncMachine?.StateMachineType != null)
                {
                    foreach (
                        MethodInfo step in asyncMachine.StateMachineType.GetMethods(
                            BindingFlags.Public
                                | BindingFlags.NonPublic
                                | BindingFlags.Instance
                                | BindingFlags.Static
                                | BindingFlags.DeclaredOnly
                        )
                    )
                    {
                        if (visited.Add(step))
                        {
                            queue.Enqueue(step);
                        }
                    }
                }
            }
        }

        private static bool BelongsTo(Type candidate, Type root)
        {
            for (Type current = candidate; current != null; current = current.DeclaringType)
            {
                if (current == root || (current.IsGenericType && current.GetGenericTypeDefinition() == root))
                {
                    return true;
                }
            }

            return false;
        }

        private static IEnumerable<MethodBase> CalledMethods(MethodBase method)
        {
            MethodBody body;

            try
            {
                body = method.GetMethodBody();
            }
            catch (Exception)
            {
                yield break;
            }

            byte[] il = body?.GetILAsByteArray();

            if (il == null || il.Length == 0)
            {
                yield break;
            }

            Module module = method.Module;
            Type[] typeArgs =
                method.DeclaringType != null && method.DeclaringType.IsGenericType
                    ? method.DeclaringType.GetGenericArguments()
                    : Type.EmptyTypes;
            Type[] methodArgs =
                method is MethodInfo mi && mi.IsGenericMethod ? mi.GetGenericArguments() : Type.EmptyTypes;

            int i = 0;

            while (i < il.Length)
            {
                OpCode op = il[i] == 0xFE ? TwoByteOpCodes[il[++i]] : OneByteOpCodes[il[i]];
                i++;

                switch (op.OperandType)
                {
                    case OperandType.InlineNone:
                        break;
                    case OperandType.ShortInlineBrTarget:
                    case OperandType.ShortInlineI:
                    case OperandType.ShortInlineVar:
                        i += 1;
                        break;
                    case OperandType.InlineVar:
                        i += 2;
                        break;
                    case OperandType.InlineMethod:
                    {
                        int token = BitConverter.ToInt32(il, i);
                        i += 4;
                        MethodBase resolved = null;

                        try
                        {
                            resolved = module.ResolveMethod(token, typeArgs, methodArgs);
                        }
                        catch (Exception) { }

                        if (resolved != null)
                        {
                            yield return resolved;
                        }

                        break;
                    }
                    case OperandType.InlineBrTarget:
                    case OperandType.InlineField:
                    case OperandType.InlineI:
                    case OperandType.InlineSig:
                    case OperandType.InlineString:
                    case OperandType.InlineTok:
                    case OperandType.InlineType:
                    case OperandType.ShortInlineR:
                        i += 4;
                        break;
                    case OperandType.InlineI8:
                    case OperandType.InlineR:
                        i += 8;
                        break;
                    case OperandType.InlineSwitch:
                    {
                        int count = BitConverter.ToInt32(il, i);
                        i += 4 + (4 * count);
                        break;
                    }
                }
            }
        }

        private static OpCode[] BuildOpCodes(bool twoByte)
        {
            var table = new OpCode[256];

            foreach (FieldInfo field in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                if (field.FieldType != typeof(OpCode))
                {
                    continue;
                }

                var op = (OpCode)field.GetValue(null);
                ushort value = unchecked((ushort)op.Value);

                if (twoByte && (value >> 8) == 0xFE)
                {
                    table[value & 0xFF] = op;
                }
                else if (!twoByte && value < 0x100)
                {
                    table[value] = op;
                }
            }

            return table;
        }
    }
}
