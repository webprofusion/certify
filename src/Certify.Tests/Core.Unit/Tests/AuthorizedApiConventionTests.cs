using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Mvc;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Certify.Tests.Core.Unit.Tests
{
    /// <summary>
    /// Being authenticated says who the caller is, not what they may do. A JWT is issued to any security principal
    /// which can sign in, whatever roles it holds, so an endpoint carrying only [AuthorizedApi] is reachable by
    /// every user of the hub. Each one therefore has to check the resource action it needs, or say in the code why
    /// it does not.
    /// </summary>
    [TestClass]
    public class AuthorizedApiConventionTests
    {
        /// <summary>
        /// Methods on ApiControllerBase which establish that the caller may perform the requested action.
        /// </summary>
        private static readonly HashSet<string> AuthorizationMethods = new(StringComparer.Ordinal)
        {
            "CheckRequestAuthorized",
            "IsAuthorized",
            "IsAccessTokenAuthorized",
            "CheckIdentifiersAuthorized",
            "ValidateManagedInstanceRequestAuthAsync",
        };

        [TestMethod]
        public void EveryAuthorizedApiEndpointChecksAResourceActionOrSaysWhyItDoesNot()
        {
            var endpoints = GetAuthorizedApiEndpoints();

            // guard against the reflection or IL walk below quietly finding nothing and passing vacuously
            Assert.IsGreaterThan(50, endpoints.Count, "expected to find the hub API's authenticated endpoints by reflection");

            var unchecked_ = new List<string>();
            var checkedCount = 0;

            foreach (var endpoint in endpoints)
            {
                if (endpoint.GetCustomAttributes(inherit: false).Any(a => a.GetType().Name == "NoResourceActionRequiredAttribute"))
                {
                    continue;
                }

                if (PerformsAuthorizationCheck(endpoint))
                {
                    checkedCount++;
                }
                else
                {
                    unchecked_.Add($"{endpoint.DeclaringType?.Name}.{endpoint.Name}");
                }
            }

            // if the IL walk were broken every endpoint would look unchecked, so confirm it recognises the many
            // which do check before trusting it to report the ones which do not
            Assert.IsGreaterThan(50, checkedCount, "expected the IL walk to recognise the endpoints which do check an action");

            Assert.IsEmpty(
                unchecked_,
                $"These [AuthorizedApi] endpoints perform no resource action check, so any authenticated principal can call them "
                    + $"regardless of role. Add a CheckRequestAuthorized call, or [NoResourceActionRequired(\"reason\")] if the "
                    + $"endpoint genuinely exposes no resource:\r\n{string.Join("\r\n", unchecked_)}");
        }

        private static List<MethodInfo> GetAuthorizedApiEndpoints()
        {
            var assembly = typeof(Certify.Server.Hub.Api.Middleware.ApiKeyAuthenticationHandler).Assembly;

            return assembly
                .GetTypes()
                .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract)
                .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                // AuthorizedApiAttribute is internal to the hub API, so it is matched by name rather than by type
                .Where(m => m.GetCustomAttributes(inherit: false).Any(a => a.GetType().Name == "AuthorizedApiAttribute"))
                .ToList();
        }

        /// <summary>
        /// True when the endpoint's own body calls one of the authorization helpers. An async method's body is
        /// compiled into a state machine, so the check is looked for where the code actually ends up.
        /// </summary>
        private static bool PerformsAuthorizationCheck(MethodInfo endpoint)
        {
            var stateMachine = endpoint.GetCustomAttribute<AsyncStateMachineAttribute>()?.StateMachineType;

            var implementation = stateMachine == null
                ? endpoint
                : stateMachine.GetMethod("MoveNext", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            if (implementation == null)
            {
                return false;
            }

            return GetCalledMethodNames(implementation).Any(AuthorizationMethods.Contains);
        }

        private static readonly Dictionary<short, OpCode> OpCodesByValue = typeof(OpCodes)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.FieldType == typeof(OpCode))
            .Select(f => (OpCode)f.GetValue(null)!)
            .ToDictionary(o => o.Value, o => o);

        /// <summary>
        /// Names of the methods called by a method body, by walking its IL and resolving each call token.
        /// </summary>
        private static IEnumerable<string> GetCalledMethodNames(MethodBase method)
        {
            var il = method.GetMethodBody()?.GetILAsByteArray();

            if (il == null)
            {
                yield break;
            }

            var module = method.Module;
            var typeArgs = method.DeclaringType?.IsGenericType == true ? method.DeclaringType.GetGenericArguments() : null;
            var methodArgs = method.IsGenericMethod ? method.GetGenericArguments() : null;

            var position = 0;

            while (position < il.Length)
            {
                short code = il[position++];

                if (code == 0xFE && position < il.Length)
                {
                    code = (short)(0xFE00 | il[position++]);
                }

                if (!OpCodesByValue.TryGetValue(code, out var opCode))
                {
                    // an opcode this walk does not know about means the rest of the stream cannot be trusted
                    yield break;
                }

                if (opCode.OperandType is OperandType.InlineMethod or OperandType.InlineTok && position + 4 <= il.Length)
                {
                    var token = BitConverter.ToInt32(il, position);

                    string? name = null;

                    try
                    {
                        name = module.ResolveMethod(token, typeArgs, methodArgs)?.Name;
                    }
                    catch (ArgumentException)
                    {
                        // InlineTok can also be a type or field token, which is not a call
                    }

                    if (name != null)
                    {
                        yield return name;
                    }
                }

                position += GetOperandSize(opCode, il, position);
            }
        }

        private static int GetOperandSize(OpCode opCode, byte[] il, int position)
        {
            switch (opCode.OperandType)
            {
                case OperandType.InlineNone:
                    return 0;
                case OperandType.ShortInlineBrTarget:
                case OperandType.ShortInlineI:
                case OperandType.ShortInlineVar:
                    return 1;
                case OperandType.InlineVar:
                    return 2;
                case OperandType.InlineBrTarget:
                case OperandType.InlineField:
                case OperandType.InlineI:
                case OperandType.InlineMethod:
                case OperandType.InlineSig:
                case OperandType.InlineString:
                case OperandType.InlineTok:
                case OperandType.InlineType:
                case OperandType.ShortInlineR:
                    return 4;
                case OperandType.InlineI8:
                case OperandType.InlineR:
                    return 8;
                case OperandType.InlineSwitch:
                    if (position + 4 > il.Length)
                    {
                        return il.Length - position;
                    }

                    return 4 + (BitConverter.ToInt32(il, position) * 4);
                default:
                    return 0;
            }
        }
    }
}
