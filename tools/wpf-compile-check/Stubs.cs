// Compile-check stand-ins for types WPF takes from NuGet packages.
namespace System.IO.Packaging {
  public enum CompressionOption { NotCompressed=-1, Normal=0, Maximum=1, Fast=2, SuperFast=3 }
  public enum EncryptionOption { None=0, RightsManagement=1 }
  public enum TargetMode { Internal=0, External=1 }
  public abstract class Package : System.IDisposable { public void Dispose(){} }
  public abstract class PackagePart { }
  public abstract class PackageProperties : System.IDisposable { public void Dispose(){} }
  public sealed class PackageRelationship { }
  public sealed class PackageRelationshipSelector { }
  public class PackageRelationshipCollection { }
  public enum PackageRelationshipSelectorType { Id=0, Type=1 }
}
namespace System.Security.Cryptography.Xml {
  public class Signature { } public class Reference { } public class DataObject { } public class SignedXml { }
}
namespace System.Xaml.Permissions {
  public sealed class XamlAccessLevel { }
  public sealed class XamlLoadPermission { }
}
