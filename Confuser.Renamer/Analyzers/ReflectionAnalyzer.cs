﻿using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Confuser.Core;
using Confuser.Core.Services;
using Confuser.Renamer.Properties;
using Confuser.Renamer.References;
using Confuser.Renamer.Services;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ILogger = Microsoft.Extensions.Logging.ILogger;

namespace Confuser.Renamer.Analyzers {
	/// <summary>
	/// This analyzer is looking for calls to the reflection API and blocks methods from being renamed if required.
	/// </summary>
	public sealed class ReflectionAnalyzer : IRenamer {
		void IRenamer.Analyze(IConfuserContext context, INameService service, IProtectionParameters parameters, IDnlibDef def) {
			if (!(def is MethodDef method) || !method.HasBody) return;

			var logger = context.Registry.GetRequiredService<ILoggerFactory>().CreateLogger(NameProtection._Id);

			Analyze(context, service, context.Registry.GetRequiredService<ITraceService>(), logger, method);
		}

		internal void Analyze(IConfuserContext context, INameService nameService, ITraceService traceService, ILogger logger, MethodDef method) {
			IMethodTrace methodTrace = null;
			IMethodTrace GetMethodTrace() {
				return methodTrace ?? (methodTrace = traceService.Trace(method));
			}

			foreach (var instr in method.Body.Instructions) {
				if (instr.OpCode.Code == Code.Call && instr.Operand is IMethodDefOrRef calledMethod) {
					if (calledMethod.DeclaringType.FullName == "System.Type") {
						Func<TypeDef, IEnumerable<IMemberDef>> getMember = null;
						if (calledMethod.Name == nameof(Type.GetMethod))
							getMember = t => t.Methods;
						else if (calledMethod.Name == nameof(Type.GetField))
							getMember = t => t.Fields;
						else if (calledMethod.Name == nameof(Type.GetProperty))
							getMember = t => t.Properties;
						else if (calledMethod.Name == nameof(Type.GetEvent))
							getMember = t => t.Events;
						else if (calledMethod.Name == nameof(Type.GetMember))
							getMember = t => Enumerable.Empty<IMemberDef>().Concat(t.Methods).Concat(t.Fields).Concat(t.Properties).Concat(t.Events);

						if (getMember != null) {
							var trace = GetMethodTrace();
							var arguments = trace.TraceArguments(instr);
							if (arguments == null) {
								logger.LogWarning(Resources.ReflectionAnalyzer_Analyze_TracingArgumentsFailed, calledMethod.FullName, method.FullName);
							} 
							else if (arguments.Length >= 2) {
								var types = GetReferencedTypes(method.Body.Instructions[arguments[0]], method, trace);
								var names = GetReferencedNames(method.Body.Instructions[arguments[1]]);

								if (!types.Any())
									types = ImmutableList.CreateRange(context.Modules.SelectMany(m => m.GetTypes()));

								foreach (var possibleMember in types.SelectMany(GetTypeAndBaseTypes).SelectMany(getMember).Where(m => names.Contains(m.Name))) {
									nameService.SetCanRename(context, possibleMember, false);
									if (!(possibleMember is IMethod) && !(possibleMember is PropertyDef) && !(possibleMember is EventDef)) continue;
									
									foreach (var reference in nameService.GetReferences(context, possibleMember).OfType<MemberOverrideReference>()) {
										nameService.SetCanRename(context, reference.BaseMemberDef, false);
									}
								}
							}
						}
					}
				}
			}
		}

		private static IEnumerable<TypeDef> GetTypeAndBaseTypes(TypeDef typeDef) {
			var currentType = typeDef;
			while (currentType != null) {
				yield return currentType;
				currentType = currentType.BaseType.ResolveTypeDef();
			}
		}

		/// <summary>
		/// This method is used to determine the types that are load onto the stack at the referenced instruction.
		/// In case the method is unable to determine all the types reliable, it will return a empty list.
		/// </summary>
		private static IReadOnlyList<TypeDef> GetReferencedTypes(Instruction instruction, MethodDef method, IMethodTrace trace) {
			if (instruction.OpCode.Code == Code.Call && instruction.Operand is IMethodDefOrRef calledMethod) {
				if (calledMethod.DeclaringType.FullName == "System.Type" && calledMethod.Name == "GetTypeFromHandle") {
					var arguments = trace.TraceArguments(instruction);
					if (arguments.Length == 1) {
						var ldTokenInstr = method.Body.Instructions[arguments[0]];
						if (ldTokenInstr.OpCode.Code == Code.Ldtoken && ldTokenInstr.Operand is TypeDef refTypeDef) {
							return new List<TypeDef>() { refTypeDef };
						}
					}
				}
			}

			return new List<TypeDef>();
		}

		private static IReadOnlyList<UTF8String> GetReferencedNames(Instruction instruction) {
			if (instruction.OpCode.Code == Code.Ldstr && instruction.Operand is string str) {
				return new List<UTF8String>() { str };
			}

			return new List<UTF8String>();
		}

		void IRenamer.PostRename(IConfuserContext context, INameService service, IProtectionParameters parameters, IDnlibDef def) { }

		void IRenamer.PreRename(IConfuserContext context, INameService service, IProtectionParameters parameters, IDnlibDef def) { }
	}
}
