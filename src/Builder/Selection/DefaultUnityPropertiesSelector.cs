﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Unity.Policy;
using Unity.Utility;

namespace Unity.Builder
{
    /// <summary>
    /// An implementation of <see cref="ISelect{PropertyInfo}"/> that is aware of
    /// the build keys used by the unity container.
    /// </summary>
    public class DefaultUnityPropertiesSelector : MemberSelectorBase<PropertyInfo, object>,
                                                  ISelect<PropertyInfo>
    {
        #region IPropertySelectorPolicy

        /// <summary>
        /// Returns sequence of properties on the given type that
        /// should be set as part of building that object.
        /// </summary>
        /// <param name="context">Current build context.</param>
        /// <returns>Sequence of <see cref="PropertyInfo"/> objects
        /// that contain the properties to set.</returns>
        public IEnumerable<object> Select(ref BuilderContext context)
            => OnSelect(ref context).Distinct(); 

        #endregion


        #region Overrides

        protected override PropertyInfo[] DeclaredMembers(Type type)
        {
#if NETSTANDARD1_0
            return type.GetPropertiesHierarchical()
                       .Where(p =>
                       {
                           if (!p.CanWrite) return false;

                           var propertyMethod = p.GetSetMethod(true) ??
                                                p.GetGetMethod(true);

                           // Skip static properties and indexers. 
                           if (propertyMethod.IsStatic || p.GetIndexParameters().Length != 0)
                               return false;

                           return true;
                       })
                      .ToArray();
#else
            return type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
                       .Where(p => p.CanWrite && p.GetIndexParameters().Length == 0)
                       .ToArray();
#endif
        }

        #endregion
    }
}
