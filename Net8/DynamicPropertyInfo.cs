using System.Globalization;
using System.Reflection;

namespace Com.H.Data.Common
{
    internal class DynamicPropertyInfo : PropertyInfo
    {

        public DynamicPropertyInfo(string propertyName, Type propertyType)
            => (this.name, this.propertyType) = (propertyName, propertyType);


        public override PropertyAttributes Attributes => throw new NotImplementedException();

        public override bool CanRead => true;

        public override bool CanWrite => true;

        private readonly Type propertyType;
        public override Type PropertyType => this.propertyType;

        public override Type DeclaringType => throw new NotImplementedException();

        private readonly string name;
        public override string Name => this.name;

        public override Type ReflectedType => throw new NotImplementedException();

        public override MethodInfo[] GetAccessors(bool nonPublic)
        {
            throw new NotImplementedException();
        }

        public override object[] GetCustomAttributes(bool inherit)
        {
            throw new NotImplementedException();
        }

        public override object[] GetCustomAttributes(Type attributeType, bool inherit)
        {
            throw new NotImplementedException();
        }

        public override MethodInfo GetGetMethod(bool nonPublic)
        {
            throw new NotImplementedException();
        }

        public override ParameterInfo[] GetIndexParameters()
        {
            throw new NotImplementedException();
        }

        public override MethodInfo GetSetMethod(bool nonPublic)
        {
            throw new NotImplementedException();
        }

        public override object? GetValue(object? obj, BindingFlags invokeAttr, Binder? binder, object?[]? index, CultureInfo? culture)
            => obj == null ? null : ((IDictionary<string, object>)obj)[this.Name];


        public override bool IsDefined(Type attributeType, bool inherit)
        {
            throw new NotImplementedException();
        }

        public override void SetValue(object? obj, 
            object? value, 
            BindingFlags invokeAttr, 
            Binder? binder, 
            object?[]? index, 
            CultureInfo? culture)
        {
            if (obj == null) return;
#pragma warning disable CS8601 // Possible null reference assignment.
            ((IDictionary<string, object>)obj)[this.Name] = value;
#pragma warning restore CS8601 // Possible null reference assignment.
        }

    }
}
