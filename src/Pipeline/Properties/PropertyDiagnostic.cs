﻿using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using Unity.Builder;
using Unity.Exceptions;
using Unity.Injection;
using Unity.Policy;
using Unity.Registration;
using Unity.Resolution;

namespace Unity
{
    public partial class PropertyDiagnostic : PropertyPipeline
    {
        #region Constructors

        public PropertyDiagnostic(UnityContainer container) 
            : base(container)
        {
            container.Defaults.Set(typeof(Func<Type, InjectionMember, PropertyInfo>), InjectionValidatingSelector);
        }

        #endregion


        #region Overrides

        public override IEnumerable<object> Select(Type type, IPolicySet registration)
        {
            HashSet<object> memberSet = new HashSet<object>();

            // Select Injected Members
            foreach (var injectionMember in ((ImplicitRegistration)registration).InjectionMembers ?? EmptyCollection)
            {
                if (injectionMember is InjectionMember<PropertyInfo, object> && memberSet.Add(injectionMember))
                    yield return injectionMember;
            }

            // Select Attributed members
            foreach (var member in type.GetDeclaredProperties())
            {
                foreach(var node in AttributeFactories)
                {
#if NET40
                    if (!member.IsDefined(node.Type, true) ||
#else
                    if (!member.IsDefined(node.Type) ||
#endif
                        !memberSet.Add(member)) continue;

                    var setter = member.GetSetMethod(true);
                    if (!member.CanWrite || null == setter)
                        yield return new InvalidRegistrationException(
                            $"Readonly property '{member.Name}' on type '{type?.Name}' is marked for injection. Readonly properties cannot be injected");

                    if (0 != member.GetIndexParameters().Length)
                        yield return new InvalidRegistrationException(
                            $"Indexer '{member.Name}' on type '{type?.Name}' is marked for injection. Indexers cannot be injected");

                    if (setter.IsStatic)
                        yield return new InvalidRegistrationException(
                            $"Static property '{member.Name}' on type '{type?.Name}' is marked for injection. Static properties cannot be injected");

                    if (setter.IsPrivate)
                        yield return new InvalidRegistrationException(
                            $"Private property '{member.Name}' on type '{type?.Name}' is marked for injection. Private properties cannot be injected");

                    if (setter.IsFamily)
                        yield return new InvalidRegistrationException(
                            $"Protected property '{member.Name}' on type '{type?.Name}' is marked for injection. Protected properties cannot be injected");

                    yield return member;
                    break;
                }
            }
        }

        protected override Expression GetResolverExpression(PropertyInfo property, object? resolver)
        {
            var ex = Expression.Variable(typeof(Exception));
            var exData = Expression.MakeMemberAccess(ex, DataProperty);
            var block = 
                Expression.Block(property.PropertyType,
                    Expression.Call(exData, AddMethod,
                        Expression.Convert(NewGuid, typeof(object)),
                        Expression.Constant(property, typeof(object))),
                Expression.Rethrow(property.PropertyType));

            return Expression.TryCatch(base.GetResolverExpression(property, resolver),
                   Expression.Catch(ex, block));
        }

        protected override ResolveDelegate<BuilderContext> GetResolverDelegate(PropertyInfo info, object? resolver)
        {
            var value = PreProcessResolver(info, resolver);
            return (ref BuilderContext context) =>
            {
                try
                {
#if NET40
                    info.SetValue(context.Existing, context.Resolve(info, value), null);
#else
                    info.SetValue(context.Existing, context.Resolve(info, value));
#endif
                    return context.Existing;
                }
                catch (Exception ex)
                {
                    ex.Data.Add(Guid.NewGuid(), info);
                    throw;
                }
            };
        }

        #endregion
    }
}
