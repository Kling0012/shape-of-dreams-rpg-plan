// Marks the compiled MOD-under-test assembly; the repro byte-loads this
// assembly's file twice to create two independent MOD copies.
namespace Culinary.ModUnderTest
{
    public sealed class AssemblyMarker { }
}
