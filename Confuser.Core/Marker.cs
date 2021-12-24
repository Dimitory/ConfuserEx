﻿using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using Confuser.Core.Project;
using dnlib.DotNet;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Confuser.Core {
	using Rules = Dictionary<Rule, IPattern>;

	/// <summary>
	///     Resolves and marks the modules with protection settings according to the rules.
	/// </summary>
	public class Marker {
		/// <summary>
		///     Annotation key of Strong Name Key.
		/// </summary>
		public static readonly object SNKey = new object();

		/// <summary>
		///     Annotation key of Strong Name Public Key.
		/// </summary>
		public static readonly object SNPubKey = new object();

		/// <summary>
		///     Annotation key of Strong Name delay signing.
		/// </summary>
		public static readonly object SNDelaySig = new object();

		/// <summary>
		///     Annotation key of Strong Name Signature Key.
		/// </summary>
		public static readonly object SNSigKey = new object();

		/// <summary>
		///     Annotation key of Strong Name Public Signature Key.
		/// </summary>
		public static readonly object SNSigPubKey = new object();

		/// <summary>
		///     Annotation key of rules.
		/// </summary>
		public static readonly object RulesKey = new object();

		/// <summary>
		///     The packers available to use.
		/// </summary>
		protected Dictionary<string, IPacker> packers;

		/// <summary>
		///     The protections available to use.
		/// </summary>
		protected Dictionary<string, IProtection> protections;

		/// <summary>
		///     Initalizes the Marker with specified protections and packers.
		/// </summary>
		/// <param name="protections">The protections.</param>
		/// <param name="packers">The packers.</param>
		public virtual void Initalize(IEnumerable<Lazy<IProtection, IProtectionMetadata>> protections,
			IEnumerable<Lazy<IPacker, IPackerMetadata>> packers) {
			this.protections = protections.ToDictionary(prot => prot.Metadata.MarkerId ?? prot.Metadata.Id,
				prot => prot.Value, StringComparer.OrdinalIgnoreCase);
			this.packers = packers.ToDictionary(packer => packer.Metadata.MarkerId ?? packer.Metadata.Id,
				packer => packer.Value, StringComparer.OrdinalIgnoreCase);
		}

		/// <summary>
		///     Fills the protection settings with the specified preset.
		/// </summary>
		/// <param name="preset">The preset.</param>
		/// <param name="settings">The settings.</param>
		void FillPreset(ProtectionPreset preset, ProtectionSettings settings) {
			foreach (var prot in protections.Values)
				if (prot.Preset != ProtectionPreset.None && prot.Preset <= preset && !settings.ContainsKey(prot))
					settings.Add(prot, new Dictionary<string, string>());
		}

		public static StrongNamePublicKey LoadSNPubKey(IConfuserContext context, string path) {
			if (path == null) return null;

			try {
				return new StrongNamePublicKey(path);
			}
			catch (Exception ex) {
				var logger = context.Registry.GetRequiredService<ILoggerFactory>().CreateLogger("core");
				logger.LogError(ex, "Cannot load the Strong Name Public Key located at: {path}", path);
				throw new ConfuserException(ex);
			}
		}

		/// <summary>
		///     Loads the Strong Name Key at the specified path with a optional password.
		/// </summary>
		/// <param name="context">The working context.</param>
		/// <param name="path">The path to the key.</param>
		/// <param name="pass">
		///     The password of the certificate at <paramref name="path" /> if
		///     it is a pfx file; otherwise, <c>null</c>.
		/// </param>
		/// <returns>The loaded Strong Name Key.</returns>
		public static StrongNameKey LoadSNKey(IConfuserContext context, string path, string pass) {
			if (path == null) return null;

			try {
				if (pass != null) //pfx
				{
					// http://stackoverflow.com/a/12196742/462805
					var cert = new X509Certificate2();
					cert.Import(path, pass, X509KeyStorageFlags.Exportable);

					var rsa = cert.PrivateKey as RSACryptoServiceProvider;
					if (rsa == null)
						throw new ArgumentException("RSA key does not present in the certificate.", "path");

					return new StrongNameKey(rsa.ExportCspBlob(true));
				}

				return new StrongNameKey(path);
			}
			catch (Exception ex) {
				var logger = context.Registry.GetRequiredService<ILoggerFactory>().CreateLogger("core");
				logger.LogCritical(ex, "Cannot load the Strong Name Key located at: {0}", path);
				throw new ConfuserException(ex);
			}
		}

		/// <summary>
		///     Loads the assembly and marks the project.
		/// </summary>
		/// <param name="proj">The project.</param>
		/// <param name="context">The working context.</param>
		/// <returns><see cref="MarkerResult" /> storing the marked modules and packer information.</returns>
		protected internal virtual MarkerResult MarkProject(ConfuserProject proj, ConfuserContext context,
			CancellationToken token) {
			var logger = context.Registry.GetRequiredService<ILoggerFactory>().CreateLogger("core");
			IPacker packer = null;
			Dictionary<string, string> packerParams = null;

			if (proj.Packer != null) {
				if (!packers.ContainsKey(proj.Packer.Id)) {
					logger.LogCritical("Cannot find packer with ID '{0}'.", proj.Packer.Id);
					throw new ConfuserException();
				}

				if (proj.Debug)
					logger.LogWarning("Generated Debug symbols might not be usable with packers!");

				packer = packers[proj.Packer.Id];
				packerParams = new Dictionary<string, string>(proj.Packer, StringComparer.OrdinalIgnoreCase);
			}

			var modules = new List<Tuple<ProjectModule, ModuleDefMD>>();
			var extModules = ImmutableArray.CreateBuilder<ReadOnlyMemory<byte>>();
			foreach (ProjectModule module in proj) {
				if (module.IsExternal) {
					var rawModule = module.LoadRaw(proj.BaseDirectory);
					extModules.Add(rawModule);
					var extModule = ModuleDefMD.Load(rawModule.ToArray(), context.InternalResolver.DefaultModuleContext);
					extModule.EnableTypeDefFindCache = true;
					context.InternalResolver.AddToCache(extModule);
					continue;
				}

				ModuleDefMD modDef = module.Resolve(proj.BaseDirectory, context.InternalResolver.DefaultModuleContext);
				token.ThrowIfCancellationRequested();

				if (proj.Debug)
					modDef.LoadPdb();

				context.InternalResolver.AddToCache(modDef);
				modules.Add(Tuple.Create(module, modDef));
			}

			foreach (var module in modules) {
				logger.LogInformation("Loading '{0}'...", module.Item1.Path);
				Rules rules = ParseRules(proj, module.Item1, context);

				context.Annotations.Set(module.Item2, SNKey,
					LoadSNKey(context,
						module.Item1.SNKeyPath == null
							? null
							: Path.Combine(proj.BaseDirectory, module.Item1.SNKeyPath), module.Item1.SNKeyPassword));
				context.Annotations.Set(module.Item2, SNSigKey, LoadSNKey(context, module.Item1.SNSigKeyPath == null ? null : Path.Combine(proj.BaseDirectory, module.Item1.SNSigKeyPath), module.Item1.SNSigKeyPassword));
				context.Annotations.Set(module.Item2, SNPubKey, LoadSNPubKey(context, module.Item1.SNPubKeyPath == null ? null : Path.Combine(proj.BaseDirectory, module.Item1.SNPubKeyPath)));
				context.Annotations.Set(module.Item2, SNSigPubKey, LoadSNPubKey(context, module.Item1.SNPubSigKeyPath == null ? null : Path.Combine(proj.BaseDirectory, module.Item1.SNPubSigKeyPath)));
				context.Annotations.Set(module.Item2, SNDelaySig, module.Item1.SNDelaySig);

				context.Annotations.Set(module.Item2, RulesKey, rules);

				foreach (var def in module.Item2.FindDefinitions()) {
					ApplyRules(context, def, rules);
					if (def is MethodDef method) {
						var a = method.CustomDebugInfos;
					}

					token.ThrowIfCancellationRequested();
				}

				// Packer parameters are stored in modules
				if (packerParams != null)
					ProtectionParameters.GetParameters(context, module.Item2)[packer] = packerParams;
			}

			return new MarkerResult(modules.Select(module => module.Item2).ToImmutableArray(), packer,
				extModules.ToImmutable());
		}

		/// <summary>
		///     Marks the member definition.
		/// </summary>
		/// <param name="member">The member definition.</param>
		/// <param name="context">The working context.</param>
		protected internal virtual void MarkMember(IDnlibDef member, IConfuserContext context) {
			ModuleDef module = ((IMemberRef)member).Module;
			var rules = context.Annotations.Get<Rules>(module, RulesKey);
			ApplyRules(context, member, rules);
		}

		/// <summary>
		///     Parses the rules' patterns.
		/// </summary>
		/// <param name="proj">The project.</param>
		/// <param name="module">The module description.</param>
		/// <param name="context">The working context.</param>
		/// <returns>Parsed rule patterns.</returns>
		/// <exception cref="System.ArgumentException">
		///     One of the rules has invalid pattern.
		/// </exception>
		protected Rules ParseRules(ConfuserProject proj, ProjectModule module, ConfuserContext context) {
			var logger = context.Registry.GetRequiredService<ILoggerFactory>().CreateLogger("core");
			var ret = new Rules();
			foreach (Rule rule in proj.Rules.Concat(module.Rules)) {
				try {
					ret.Add(rule, PatternParser.Parse(rule.Pattern, logger));
				}
				catch (InvalidPatternException ex) {
					logger.LogCritical(ex, "Invalid rule pattern: {0}.", rule.Pattern);
					throw new ConfuserException(ex);
				}

				foreach (var setting in rule) {
					if (!protections.ContainsKey(setting.Id)) {
						logger.LogCritical("Cannot find protection with ID '{0}'.", setting.Id);
						throw new ConfuserException();
					}
				}
			}

			return ret;
		}

		/// <summary>
		///     Applies the rules to the target definition.
		/// </summary>
		/// <param name="context">The working context.</param>
		/// <param name="target">The target definition.</param>
		/// <param name="rules">The rules.</param>
		/// <param name="baseSettings">The base settings.</param>
		protected void ApplyRules(IConfuserContext context, IDnlibDef target, Rules rules,
			ProtectionSettings baseSettings = null) {
			var ret = baseSettings == null ? new ProtectionSettings() : new ProtectionSettings(baseSettings);
			foreach (var i in rules) {
				if (!(bool)i.Value.Evaluate(target)) continue;

				if (!i.Key.Inherit)
					ret.Clear();

				FillPreset(i.Key.Preset, ret);
				foreach (var prot in i.Key) {
					if (prot.Action == SettingItemAction.Add)
						ret[protections[prot.Id]] =
							new Dictionary<string, string>(prot, StringComparer.OrdinalIgnoreCase);
					else
						ret.Remove(protections[prot.Id]);
				}
			}

			ProtectionParameters.SetParameters(context, target, ret);
		}
	}
}
