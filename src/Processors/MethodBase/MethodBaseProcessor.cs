﻿using System;
using System.Reflection;
using Unity.Builder;
using Unity.Policy;

namespace Unity.Processors
{
    public abstract partial class MethodBaseProcessor<TMemberInfo> : MemberProcessor<TMemberInfo, object[]>
                                                 where TMemberInfo : MethodBase
    {
        #region Constructors

        protected MethodBaseProcessor(IPolicySet policySet, Type attribute)
            : base(policySet, new[]
            {
                new AttributeFactoryNode(attribute, null, null),

                new AttributeFactoryNode(typeof(DependencyAttribute),
                    (ExpressionParameterAttributeFactory)DependencyExpressionFactory,
                    (ResolutionParameterAttributeFactory)DependencyResolverFactory),

                new AttributeFactoryNode(typeof(OptionalDependencyAttribute),
                    (ExpressionParameterAttributeFactory)OptionalDependencyExpressionFactory,
                    (ResolutionParameterAttributeFactory)OptionalDependencyResolverFactory),
            })
        {
        }

        #endregion


        #region Overrides

        protected override Type MemberType(TMemberInfo info) => info.DeclaringType;

        #endregion


        #region Implementation

        private object PreProcessResolver(ParameterInfo parameter, object resolver)
        {
            switch (resolver)
            {
                case IResolve policy:
                    return (ResolveDelegate<BuilderContext>)policy.Resolve;

                case IResolverFactory<ParameterInfo> factory:
                    return factory.GetResolver<BuilderContext>(parameter);

                case Type type:
                    return typeof(Type) == parameter.ParameterType
                        ? type : (object)parameter;
            }

            return resolver;
        }

        #endregion
    }
}
