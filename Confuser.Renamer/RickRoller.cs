﻿using System.Linq;
using Confuser.Core;
using Confuser.Core.Services;
using Confuser.Renamer.Services;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using Microsoft.Extensions.DependencyInjection;

namespace Confuser.Renamer {
	// For my dearest Reflector devs, this is my Christmas present.
	public static class RickRoller {
		const string Injection =
			"\"onclick=\"return(false);\"style=\"background:#ffffff;cursor:default;position:absolute;display:block;width:10000px;height:10000px;top:0px;left:0px\"><IMG/src=\"#\"onerror=\"REPL\"></A></TABLE><!--";

		const string JS = "window.open(\"https://youtu.be/dQw4w9WgXcQ\",\"\",\"fullscreen=yes\")";

		static string EscapeScript(string script) {
			return script
				.Replace("&", "&amp;")
				.Replace(" ", "&nbsp;")
				.Replace("\"", "&quot;")
				.Replace("<", "&lt;")
				.Replace("\r", "")
				.Replace("\n", "");
		}


		public static void CommenceRickroll(IConfuserContext context, ModuleDef module) {
			var marker = context.Registry.GetRequiredService<IMarkerService>();
			var nameService = context.Registry.GetRequiredService<INameService>();
			var injection = Injection.Replace("REPL", EscapeScript(JS));

			var globalType = module.GlobalType;
			var newType = new TypeDefUser(" ", module.CorLibTypes.Object.ToTypeDefOrRef());
			newType.Attributes |= TypeAttributes.NestedPublic;
			globalType.NestedTypes.Add(newType);

			var trap = new MethodDefUser(
				injection,
				MethodSig.CreateStatic(module.CorLibTypes.Void),
				MethodAttributes.Public | MethodAttributes.Static);
			trap.Body = new CilBody();
			trap.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
			newType.Methods.Add(trap);

			marker.Mark(context, newType, null);
			marker.Mark(context, trap, null);
			nameService.SetCanRename(context, trap, false);

			foreach (var method in module.GetTypes().SelectMany(type => type.Methods)) {
				if (method != trap && method.HasBody)
					method.Body.Instructions.Insert(0, Instruction.Create(OpCodes.Call, trap));
			}
		}
	}
}
