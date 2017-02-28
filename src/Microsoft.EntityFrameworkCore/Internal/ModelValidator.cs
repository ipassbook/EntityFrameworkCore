// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore.ChangeTracking.Internal;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Conventions.Internal;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using Microsoft.EntityFrameworkCore.Utilities;

namespace Microsoft.EntityFrameworkCore.Internal
{
    /// <summary>
    ///     This API supports the Entity Framework Core infrastructure and is not intended to be used 
    ///     directly from your code. This API may change or be removed in future releases.
    /// </summary>
    public abstract class ModelValidator : IModelValidator
    {
        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used 
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public virtual void Validate(IModel model)
        {
            EnsureNoShadowEntities(model);
            EnsureNonNullPrimaryKeys(model);
            EnsureNoShadowKeys(model);
            EnsureClrInheritance(model);
            EnsureChangeTrackingStrategy(model);
        }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used 
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        protected virtual void EnsureNoShadowEntities([NotNull] IModel model)
        {
            var firstShadowEntity = model.GetEntityTypes().FirstOrDefault(entityType => !entityType.HasClrType());
            if (firstShadowEntity != null)
            {
                ShowError(CoreStrings.ShadowEntity(firstShadowEntity.Name));
            }
        }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used 
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        protected virtual void EnsureNoShadowKeys([NotNull] IModel model)
        {
            foreach (var entityType in model.GetEntityTypes().Where(t => t.ClrType != null))
            {
                foreach (var key in entityType.GetDeclaredKeys())
                {
                    if (key.Properties.Any(p => p.IsShadowProperty))
                    {
                        var referencingFk = key.GetReferencingForeignKeys().FirstOrDefault();
                        var conventionalKey = key as Key;
                        if (referencingFk != null
                            && conventionalKey != null
                            && ConfigurationSource.Convention.Overrides(conventionalKey.GetConfigurationSource()))
                        {
                            ShowError(CoreStrings.ReferencedShadowKey(
                                referencingFk.DeclaringEntityType.DisplayName() +
                                (referencingFk.DependentToPrincipal == null
                                    ? ""
                                    : "." + referencingFk.DependentToPrincipal.Name),
                                entityType.DisplayName() +
                                (referencingFk.PrincipalToDependent == null
                                    ? ""
                                    : "." + referencingFk.PrincipalToDependent.Name),
                                Property.Format(referencingFk.Properties, includeTypes: true),
                                Property.Format(entityType.FindPrimaryKey().Properties, includeTypes: true)));
                            continue;
                        }

                        ShowWarning(CoreStrings.ShadowKey(
                            Property.Format(key.Properties),
                            entityType.DisplayName(),
                            Property.Format(key.Properties)));
                    }
                }
            }
        }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used 
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        protected virtual void EnsureNonNullPrimaryKeys([NotNull] IModel model)
        {
            Check.NotNull(model, nameof(model));

            var entityTypeWithNullPk = model.GetEntityTypes().FirstOrDefault(et => et.FindPrimaryKey() == null);
            if (entityTypeWithNullPk != null)
            {
                ShowError(CoreStrings.EntityRequiresKey(entityTypeWithNullPk.Name));
            }
        }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used 
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        protected virtual void EnsureClrInheritance([NotNull] IModel model)
        {
            var validEntityTypes = new HashSet<IEntityType>();
            foreach (var entityType in model.GetEntityTypes())
            {
                EnsureClrInheritance(model, entityType, validEntityTypes);
            }
        }

        private void EnsureClrInheritance(IModel model, IEntityType entityType, HashSet<IEntityType> validEntityTypes)
        {
            if (validEntityTypes.Contains(entityType))
            {
                return;
            }

            var baseClrType = entityType.ClrType?.GetTypeInfo().BaseType;
            while (baseClrType != null)
            {
                var baseEntityType = model.FindEntityType(baseClrType);
                if (baseEntityType != null)
                {
                    if (!baseEntityType.IsAssignableFrom(entityType))
                    {
                        ShowError(CoreStrings.InconsistentInheritance(entityType.DisplayName(), baseEntityType.DisplayName()));
                    }
                    EnsureClrInheritance(model, baseEntityType, validEntityTypes);
                    break;
                }
                baseClrType = baseClrType.GetTypeInfo().BaseType;
            }

            if (entityType.ClrType?.IsInstantiable() == false
                && !entityType.GetDerivedTypes().Any())
            {
                ShowError(CoreStrings.AbstractLeafEntityType(entityType.DisplayName()));
            }

            validEntityTypes.Add(entityType);
        }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used 
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        protected virtual void EnsureChangeTrackingStrategy([NotNull] IModel model)
        {
            Check.NotNull(model, nameof(model));

            var detectChangesNeeded = false;
            foreach (var entityType in model.GetEntityTypes())
            {
                var changeTrackingStrategy = entityType.GetChangeTrackingStrategy();
                if (changeTrackingStrategy == ChangeTrackingStrategy.Snapshot)
                {
                    detectChangesNeeded = true;
                }

                var errorMessage = entityType.CheckChangeTrackingStrategy(changeTrackingStrategy);
                if (errorMessage != null)
                {
                    ShowError(errorMessage);
                }
            }

            if (!detectChangesNeeded)
            {
                (model as IMutableModel)?.GetOrAddAnnotation(ChangeDetector.SkipDetectChangesAnnotation, "true");
            }
        }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used 
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        protected virtual void ShowError([NotNull] string message)
        {
            throw new InvalidOperationException(message);
        }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used 
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        protected abstract void ShowWarning([NotNull] string message);
    }
}