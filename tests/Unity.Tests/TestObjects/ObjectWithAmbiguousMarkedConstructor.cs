﻿
using Unity.Tests.TestDoubles;

namespace Unity.Tests.TestObjects
{
    internal class ObjectWithAmbiguousMarkedConstructor
    {
        public ObjectWithAmbiguousMarkedConstructor()
        {
        }

        public ObjectWithAmbiguousMarkedConstructor(int first, string second, float third)
        {
        }

        [InjectionConstructor]
        public ObjectWithAmbiguousMarkedConstructor(string first, string second, int third)
        {
        }
    }
}
