﻿using System;
using System.Diagnostics;
using System.Reflection;
using System.Text.RegularExpressions;

namespace Confuser.Optimizations.CompileRegex.Compiler {
	internal static class ReflectionUtilities {
		internal static Type GetRegexType(string name) {
			var regexAssembly = typeof(Regex).Assembly;
			var fullName = "System.Text.RegularExpressions." + name;
			var resultType = regexAssembly.GetType("System.Text.RegularExpressions." + name, true, false);

			return resultType;
		}

		internal static FieldInfo GetField(Type declaringType, string name, params string[] altNames) {
			if (declaringType == null) throw new ArgumentNullException(nameof(declaringType));

			const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

			var resultField = declaringType.GetField(name, flags);
			if (resultField == null)
				foreach (var altName in altNames) {
					resultField = declaringType.GetField(altName, flags);
					if (resultField != null) break;
				}
			
			return resultField;
		}

		internal static FieldInfo GetInternalField(Type declaringType, params string[] names) {
			if (declaringType == null) throw new ArgumentNullException(nameof(declaringType));

			FieldInfo resultField = null;
			foreach (var name in names) {
				resultField = declaringType.GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
				if (resultField != null) break;
			}

			return resultField;
		}

		internal static MethodInfo GetMethod(Type declaringType, string name, params Type[] parameters) {
			if (declaringType == null) throw new ArgumentNullException(nameof(declaringType));
			if (parameters == null) throw new ArgumentNullException(nameof(parameters));

			var resultMethod = declaringType.GetMethod(name,
				BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, parameters, null);

			return resultMethod;
		}

		internal static MethodInfo GetStaticMethod(Type declaringType, string name, params Type[] parameters) {
			if (declaringType == null) throw new ArgumentNullException(nameof(declaringType));
			if (parameters == null) throw new ArgumentNullException(nameof(parameters));

			var resultMethod = declaringType.GetMethod(name,
				BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, parameters, null);

			return resultMethod;
		}

		internal static ConstructorInfo GetInstanceConstructor(Type declaringType) {
			if (declaringType == null) throw new ArgumentNullException(nameof(declaringType));

			var constructor = declaringType.GetConstructor(
				BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public, null,
				CallingConventions.Standard | CallingConventions.HasThis,
				Type.EmptyTypes, null);

			return constructor;
		}
	}
}
