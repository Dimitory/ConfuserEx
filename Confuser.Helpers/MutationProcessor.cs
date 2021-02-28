﻿using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using Confuser.Core.Services;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using Microsoft.Extensions.DependencyInjection;

namespace Confuser.Helpers {
	public delegate IReadOnlyList<Instruction> PlaceholderProcessor(ModuleDef module, MethodDef method,
		IReadOnlyList<Instruction> arguments);

	public delegate IReadOnlyList<Instruction> CryptProcessor(ModuleDef module, MethodDef method, Local block,
		Local key);

	public delegate IReadOnlyList<Instruction> ValueProcessor(ModuleDef module, MethodDef method, Instruction callInstruction);

	public class MutationProcessor : IMethodInjectProcessor {
		private const string MutationClassName = "Confuser.Mutation";

		private ITraceService TraceService { get; }
		private ModuleDef TargetModule { get; }
		public IReadOnlyDictionary<MutationField, int> KeyFieldValues { get; set; }
		public IReadOnlyDictionary<MutationField, LateMutationFieldUpdate> LateKeyFieldValues { get; set; }
		public PlaceholderProcessor PlaceholderProcessor { get; set; }
		public CryptProcessor CryptProcessor { get; set; }
		public ValueProcessor ValueProcessor { get; set; }

		public MutationProcessor(IServiceProvider services, ModuleDef targetModule) :
			this(services.GetRequiredService<ITraceService>(), targetModule) { }

		public MutationProcessor(ITraceService traceService, ModuleDef targetModule) {
			TraceService = traceService ?? throw new ArgumentNullException(nameof(traceService));
			TargetModule = targetModule ?? throw new ArgumentNullException(nameof(targetModule));
		}

		public void Process(MethodDef method) {
			Debug.Assert(method != null, $"{nameof(method)} != null");
			Debug.Assert(method.HasBody, $"{nameof(method)}.HasBody");

			if (method == null || !method.HasBody || !method.Body.HasInstructions) return;

			var instructions = method.Body.Instructions;
			for (var i = 0; i < instructions.Count; i++) {
				var instr = instructions[i];

				if (instr.OpCode == OpCodes.Ldsfld) {
					if (instr.Operand is IField loadedField &&
					    loadedField.DeclaringType.FullName == MutationClassName) {
						if (!ProcessKeyField(method, instr, loadedField))
							throw new InvalidOperationException("Unexpected load field operation to Mutation class!");
					}
				}
				else if (instr.OpCode == OpCodes.Call) {
					if (instr.Operand is IMethod calledMethod &&
					    calledMethod.DeclaringType.FullName == MutationClassName) {
						if (!ReplacePlaceholder(method, instr, calledMethod, ref i) &&
						    !ReplaceCrypt(method, instr, calledMethod, ref i) &&
						    !ReplaceValue(method, instr, calledMethod, ref i))
							throw new InvalidOperationException("Unexpected call operation to Mutation class!");
					}
				}
			}
		}

		private bool ProcessKeyField(MethodDef method, Instruction instr, IField field) {
			Debug.Assert(method != null, $"{nameof(method)} != null");
			Debug.Assert(instr != null, $"{nameof(instr)} != null");
			Debug.Assert(field != null, $"{nameof(field)} != null");

			if (field.Name?.Length >= 5 && field.Name.StartsWith("KeyI")) {
				var number = field.Name.String.AsSpan().Slice(start: 4, length: (field.Name.Length == 5 ? 1 : 2));
				if (int.TryParse(number.ToString(), out var value)) {
					MutationField mutationField;
					switch (value) {
						case 0:
							mutationField = MutationField.KeyI0;
							break;
						case 1:
							mutationField = MutationField.KeyI1;
							break;
						case 2:
							mutationField = MutationField.KeyI2;
							break;
						case 3:
							mutationField = MutationField.KeyI3;
							break;
						case 4:
							mutationField = MutationField.KeyI4;
							break;
						case 5:
							mutationField = MutationField.KeyI5;
							break;
						case 6:
							mutationField = MutationField.KeyI6;
							break;
						case 7:
							mutationField = MutationField.KeyI7;
							break;
						case 8:
							mutationField = MutationField.KeyI8;
							break;
						case 9:
							mutationField = MutationField.KeyI9;
							break;
						case 10:
							mutationField = MutationField.KeyI10;
							break;
						case 11:
							mutationField = MutationField.KeyI11;
							break;
						case 12:
							mutationField = MutationField.KeyI12;
							break;
						case 13:
							mutationField = MutationField.KeyI13;
							break;
						case 14:
							mutationField = MutationField.KeyI14;
							break;
						case 15:
							mutationField = MutationField.KeyI15;
							break;
						default: return false;
					}

					if (KeyFieldValues != null && KeyFieldValues.TryGetValue(mutationField, out var keyValue)) {
						instr.OpCode = OpCodes.Ldc_I4;
						instr.Operand = keyValue;
						return true;
					}
					else if (LateKeyFieldValues != null &&
					         LateKeyFieldValues.TryGetValue(mutationField, out var lateUpdate)) {
						lateUpdate.AddUpdateInstruction(method, instr);
						// Setting a dummy value, so the reference to the Mutation class is not injected.
						instr.OpCode = OpCodes.Ldc_I4_0;
						instr.Operand = null;
						return true;
					}
					else {
						throw new InvalidOperationException(
							$"Code contains request to mutation key {field.Name}, but the value for this field is not set.");
					}
				}
			}

			return false;
		}

		private bool ReplacePlaceholder(MethodDef method, Instruction instr, IMethod calledMethod, ref int index) {
			Debug.Assert(method != null, $"{nameof(method)} != null");
			Debug.Assert(instr != null, $"{nameof(instr)} != null");
			Debug.Assert(calledMethod != null, $"{nameof(calledMethod)} != null");

			if (calledMethod.Name == "Placeholder") {
				if (PlaceholderProcessor == null)
					throw new InvalidOperationException(
						"Found mutation placeholder, but there is no processor defined.");
				var trace = TraceService.Trace(method);
				var initialLoadInstructions = new List<Instruction>();
				var pendingInstructions = new Queue<Instruction>();
				pendingInstructions.Enqueue(instr);
				while (pendingInstructions.Count > 0) {
					var currentInstr = pendingInstructions.Dequeue();
					int[] argIndexes = trace.TraceArguments(currentInstr);
					if (argIndexes == null) 
						throw new InvalidOperationException("Failed to trace placeholder argument.");

					if (argIndexes.Length == 0)
						initialLoadInstructions.Add(currentInstr);

					foreach (int argIndex in argIndexes)
						pendingInstructions.Enqueue(method.Body.Instructions[argIndex]);
				}

				var firstArgIndex = initialLoadInstructions.Select(method.Body.Instructions.IndexOf).Min();
				var arg = method.Body.Instructions.Skip(firstArgIndex).Take(index - firstArgIndex)
					.ToImmutableArray();

				// Remove all the loading instructions for the arguments.
				for (int j = 0; j < arg.Length; j++)
					method.Body.RemoveInstruction(firstArgIndex);

				// Insert the new instructions for the placeholder
				var replaceArgs = PlaceholderProcessor(TargetModule, method, arg);
				method.Body.InsertPrefixInstructions(firstArgIndex, replaceArgs);
				
				// Remove the call to the placeholder function
				method.Body.RemoveInstruction(instr);

				return true;
			}

			return false;
		}

		private bool ReplaceCrypt(MethodDef method, Instruction instr, IMethod calledMethod, ref int index) {
			Debug.Assert(method != null, $"{nameof(method)} != null");
			Debug.Assert(instr != null, $"{nameof(instr)} != null");
			Debug.Assert(calledMethod != null, $"{nameof(calledMethod)} != null");

			if (calledMethod.Name == "Crypt") {
				if (CryptProcessor == null)
					throw new InvalidOperationException("Found mutation crypt, but not processor defined.");

				var instrIndex = method.Body.Instructions.IndexOf(instr);
				var ldBlock = method.Body.Instructions[instrIndex - 2];
				var ldKey = method.Body.Instructions[instrIndex - 1];
				Debug.Assert(ldBlock.OpCode == OpCodes.Ldloc && ldKey.OpCode == OpCodes.Ldloc);

				method.Body.Instructions.RemoveAt(instrIndex);
				method.Body.Instructions.RemoveAt(instrIndex - 1);
				method.Body.Instructions.RemoveAt(instrIndex - 2);

				var cryptInstr = CryptProcessor(TargetModule, method, (Local)ldBlock.Operand, (Local)ldKey.Operand);
				for (var i = 0; i < cryptInstr.Count; i++) {
					method.Body.Instructions.Insert(instrIndex - 2 + i, cryptInstr[i]);
				}

				index += cryptInstr.Count - 3;

				return true;
			}

			return false;
		}

		private bool ReplaceValue(MethodDef method, Instruction instr, IMethod calledMethod, ref int index) {
			Debug.Assert(method != null, $"{nameof(method)} != null");
			Debug.Assert(instr != null, $"{nameof(instr)} != null");
			Debug.Assert(calledMethod != null, $"{nameof(calledMethod)} != null");

			if (calledMethod.Name == "Value") {
				if (ValueProcessor == null)
					throw new InvalidOperationException("Found mutation crypt, but not processor defined.");

				var replaceInstructions = ValueProcessor(TargetModule, method, instr);
				for (var i = 0; i < replaceInstructions.Count; i++) 
					method.Body.Instructions.Insert(index + i + 1, replaceInstructions[i]);
				method.Body.Instructions.RemoveAt(index);
				method.Body.ReplaceReference(instr, replaceInstructions[0]);

				index += replaceInstructions.Count - 1;

				return true;
			}

			return false;
		}
	}
}
