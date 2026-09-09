using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Certify.Tests.Core.Unit.Tests
{
    /// <summary>
    /// Being authenticated says who the caller is, not what they may do. A JWT is issued to any security principal
    /// which can sign in, whatever roles it holds, so an endpoint which only requires authentication is reachable
    /// by every user of the hub. Each one therefore has to check the resource action it needs, or say in the code
    /// why it does not.
    ///
    /// This covers both [AuthorizedApi] and plain [Authorize], because the distinction between them is which
    /// credentials are accepted, not what the caller is allowed to do with them.
    /// </summary>
    [TestClass]
    public class AuthenticatedEndpointConventionTests
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
        public void EveryAuthenticatedEndpointChecksAResourceActionOrSaysWhyItDoesNot()
        {
            var endpoints = GetAuthenticatedEndpoints();

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
                $"These authenticated endpoints perform no resource action check, so any authenticated principal can call "
                    + $"them regardless of role. Add a CheckRequestAuthorized call, or [NoResourceActionRequired(\"reason\")] "
                    + $"if the endpoint genuinely exposes no resource:\r\n{string.Join("\r\n", unchecked_)}");
        }

        /// <summary>
        /// Authorization methods which answer for the caller of the current request, and so read CurrentAuthContext.
        /// Both refuse outright when there is no authenticated principal, whatever credentials were presented.
        /// </summary>
        private static readonly HashSet<string> CallerAuthorizationMethods = new(StringComparer.Ordinal)
        {
            "CheckRequestAuthorized",
            "IsAuthorized",
        };

        /// <summary>
        /// An endpoint which authorizes the caller has to be able to identify one, and only two things do that:
        /// the authentication middleware, which runs for an endpoint carrying an authorization requirement, and
        /// IdentifyOptionalCallerAsync, for one deliberately reachable without credentials. An endpoint with
        /// neither reads CurrentAuthContext as null however good the caller's credentials were, and refuses
        /// every request - which is not a difference the endpoint's own code makes visible.
        /// </summary>
        [TestMethod]
        public void EveryEndpointWhichAuthorizesTheCallerCanIdentifyOne()
        {
            var endpoints = GetControllerEndpoints();

            // guard against the reflection or IL walk below quietly finding nothing and passing vacuously
            Assert.IsGreaterThan(50, endpoints.Count, "expected to find the hub API's endpoints by reflection");

            var unidentifiable = new List<string>();
            var authorizingCount = 0;

            foreach (var endpoint in endpoints)
            {
                if (!Calls(endpoint, CallerAuthorizationMethods))
                {
                    continue;
                }

                authorizingCount++;

                var isAuthenticated = endpoint.GetCustomAttributes(inherit: false).Any(a => a is AuthorizeAttribute)
                    || endpoint.DeclaringType?.GetCustomAttributes(inherit: true).Any(a => a is AuthorizeAttribute) == true;

                if (!isAuthenticated && !Calls(endpoint, CallerIdentificationMethods))
                {
                    unidentifiable.Add($"{endpoint.DeclaringType?.Name}.{endpoint.Name}");
                }
            }

            Assert.IsGreaterThan(50, authorizingCount, "expected the IL walk to recognise the endpoints which authorize their caller");

            Assert.IsEmpty(
                unidentifiable,
                $"These endpoints authorize the current caller but nothing authenticates one for them, so CurrentAuthContext "
                    + $"is always null and they refuse every request. Add [AuthorizedApi], or call IdentifyOptionalCallerAsync "
                    + $"if the endpoint is meant to be reachable anonymously:\r\n{string.Join("\r\n", unidentifiable)}");
        }

        private static readonly HashSet<string> CallerIdentificationMethods = new(StringComparer.Ordinal)
        {
            "IdentifyOptionalCallerAsync",
        };

        /// <summary>
        /// Every routed action on the hub API's controllers, authenticated or not.
        /// </summary>
        private static List<MethodInfo> GetControllerEndpoints()
        {
            var assembly = typeof(Certify.Server.Hub.Api.Middleware.ApiKeyAuthenticationHandler).Assembly;

            return assembly
                .GetTypes()
                .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract)
                .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                .Where(m => m.GetCustomAttributes(inherit: false).Any(a => a is Microsoft.AspNetCore.Mvc.Routing.HttpMethodAttribute))
                .ToList();
        }

        private static bool Calls(MethodInfo endpoint, HashSet<string> methodNames)
        {
            return Calls(endpoint, endpoint.DeclaringType, methodNames, MaxHelperDepth, []);
        }

        private static bool Calls(MethodBase method, Type? controllerType, HashSet<string> methodNames, int depth, HashSet<MethodBase> visited)
        {
            if (depth < 0 || !visited.Add(method))
            {
                return false;
            }

            var implementation = GetImplementation(method);

            if (implementation == null)
            {
                return false;
            }

            var called = GetCalledMethods(implementation).ToList();

            if (called.Any(m => methodNames.Contains(m.Name)))
            {
                return true;
            }

            return called
                .Where(m => m.DeclaringType != null && m.DeclaringType == controllerType)
                .Any(m => Calls(m, controllerType, methodNames, depth - 1, visited));
        }

        private static List<MethodInfo> GetAuthenticatedEndpoints()
        {
            var assembly = typeof(Certify.Server.Hub.Api.Middleware.ApiKeyAuthenticationHandler).Assembly;

            return assembly
                .GetTypes()
                .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract)
                .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                // AuthorizedApiAttribute derives from AuthorizeAttribute, so this covers it as well as plain [Authorize]
                .Where(m => m.GetCustomAttributes(inherit: false).Any(a => a is AuthorizeAttribute))
                .ToList();
        }

        /// <summary>
        /// How far the walk follows calls into an endpoint's own private helpers before giving up. Endpoints
        /// routinely authorize through a helper on the same controller (an endpoint which delegates its whole body
        /// to another action, or one whose authorization has several accepted routes), and a walk which only looked
        /// at the endpoint body would report those as unchecked. A small bound keeps the search cheap and stops it
        /// wandering out of the controller.
        /// </summary>
        private const int MaxHelperDepth = 3;

        /// <summary>
        /// True when the endpoint authorizes, either in its own body or through a helper declared on the same
        /// controller. An async method's body is compiled into a state machine, so the check is looked for where
        /// the code actually ends up.
        /// </summary>
        private static bool PerformsAuthorizationCheck(MethodInfo endpoint)
        {
            return PerformsAuthorizationCheck(endpoint, endpoint.DeclaringType, MaxHelperDepth, []);
        }

        private static bool PerformsAuthorizationCheck(MethodBase method, Type? controllerType, int depth, HashSet<MethodBase> visited)
        {
            if (depth < 0 || !visited.Add(method))
            {
                return false;
            }

            var implementation = GetImplementation(method);

            if (implementation == null)
            {
                return false;
            }

            var called = GetCalledMethods(implementation).ToList();

            if (called.Any(m => AuthorizationMethods.Contains(m.Name)))
            {
                return true;
            }

            // follow calls into helpers on the same controller, so authorization reached indirectly still counts
            return called
                .Where(m => m.DeclaringType != null && m.DeclaringType == controllerType)
                .Any(m => PerformsAuthorizationCheck(m, controllerType, depth - 1, visited));
        }

        /// <summary>
        /// The method body which actually holds the code: an async method's is its state machine's MoveNext.
        /// </summary>
        private static MethodBase? GetImplementation(MethodBase method)
        {
            var stateMachine = method.GetCustomAttribute<AsyncStateMachineAttribute>()?.StateMachineType;

            return stateMachine == null
                ? method
                : stateMachine.GetMethod("MoveNext", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        }

        private static readonly Dictionary<short, OpCode> OpCodesByValue = typeof(OpCodes)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.FieldType == typeof(OpCode))
            .Select(f => (OpCode)f.GetValue(null)!)
            .ToDictionary(o => o.Value, o => o);

        /// <summary>
        /// The methods called by a method body, by walking its IL and resolving each call token.
        /// </summary>
        private static IEnumerable<MethodBase> GetCalledMethods(MethodBase method)
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

                    MethodBase? called = null;

                    try
                    {
                        called = module.ResolveMethod(token, typeArgs, methodArgs);
                    }
                    catch (ArgumentException)
                    {
                        // InlineTok can also be a type or field token, which is not a call
                    }

                    if (called != null)
                    {
                        yield return called;
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
