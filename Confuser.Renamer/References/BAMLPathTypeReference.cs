﻿using System;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using Confuser.Core;
using Confuser.Renamer.BAML;
using Confuser.Renamer.Services;
using dnlib.DotNet;

namespace Confuser.Renamer.References {
	internal sealed class BAMLPathTypeReference : INameReference<TypeDef> {
		PropertyPathPartUpdater? PropertyInfo { get; }
		PropertyPathIndexUpdater? IndexerInfo { get; }
		private readonly TypeSig sig;
		private readonly BAMLAnalyzer.XmlNsContext xmlnsCtx;

		public bool ShouldCancelRename => false;

		private BAMLPathTypeReference(BAMLAnalyzer.XmlNsContext xmlnsCtx, TypeSig sig) {
			this.xmlnsCtx = xmlnsCtx ?? throw new ArgumentNullException(nameof(xmlnsCtx));
			this.sig = sig ?? throw new ArgumentNullException(nameof(sig));
		}

		/// <inheritdoc />
		public bool DelayRenaming(IConfuserContext context, INameService service) => false;

		public BAMLPathTypeReference(BAMLAnalyzer.XmlNsContext xmlnsCtx, TypeSig sig, PropertyPathIndexUpdater indexerInfo) : this(xmlnsCtx, sig) => 
			IndexerInfo = indexerInfo;

		public BAMLPathTypeReference(BAMLAnalyzer.XmlNsContext xmlnsCtx, TypeSig sig, PropertyPathPartUpdater propertyInfo) : this(xmlnsCtx, sig) => 
			PropertyInfo = propertyInfo;

		public bool UpdateNameReference(IConfuserContext context, INameService service) {
			string name = sig.ReflectionName;
			string prefix = xmlnsCtx.GetPrefix(sig.ReflectionNamespace,
				sig.ToBasicTypeDefOrRef().ResolveTypeDefThrow().Module.Assembly);
			if (!string.IsNullOrEmpty(prefix))
				name = prefix + ":" + name;

			if (IndexerInfo != null) {
				var info = IndexerInfo.Value;
				if (string.Equals(info.ParenString, name, StringComparison.Ordinal)) return false;
				info.ParenString = name;
			}
			else {
				Debug.Assert(PropertyInfo != null, nameof(PropertyInfo) + " != null");
				var info = PropertyInfo.Value;
				var propertyName = info.GetPropertyName();
				var newName = string.Format(CultureInfo.InvariantCulture, "({0}.{1})", name, propertyName);
				if (string.Equals(info.Name, newName, StringComparison.Ordinal)) return false;
				info.Name = newName;
			}

			return true;
		}

		public override string ToString() => ToString(null, null);

		public string ToString(IConfuserContext context, INameService nameService) {
			var builder = new StringBuilder();
			builder.Append("BAML Path Type Reference").Append("(");
			if (PropertyInfo.HasValue)
				builder.Append("Property Info").Append("(").AppendHashedIdentifier("Name", PropertyInfo.Value.Name).Append(")");
			else if (IndexerInfo.HasValue)
				builder.Append("Indexer Info").Append("(").AppendHashedIdentifier("Indexer", IndexerInfo.Value.ParenString).Append(")");
			builder.Append("; ");
			builder.Append("Type Signature").Append("(").AppendHashedIdentifier("Name", sig.ReflectionFullName).Append(")");
			builder.Append(")");
			return builder.ToString();
		}
	}
}
