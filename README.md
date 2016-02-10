A set of BuildCustomizations for preprocessing/preassembling DynASM source files
for inclusion in C/C++ source files. Should work for Visual Studio 2010 and
newer.

## Installation

I’d like to work under the assumption that if you’re smart enough to be working
on a project that requires DynASM, you’re smarter enough to figure this out on
your own without me over-explaining it. Either add the directory of this repo to
your BuildCustomization search paths or copy the contents of this repo into your
default BuildCustomizations folder. For Visual Studio 2010, this would be
something like:

C:\Program Files (x86)\MsBuild\Microsoft.Cpp\v4.0\BuildCustomizations

If everything is done correctly, you should see a **DynASM** section under your
project configuration properties. (and in the Properties of the .dasc file
itself) If you have any difficulties, there’s some helpful comments in
dasm.props. If you still can’t figure it out or you run into issues, you could
always post an issue here or message me for help.

## Usage Notes

By default, the build customization will operate on .dasc files, turning them
into .inl files for their **inclusion** in C/C++ source files. There was a valid
reason I had it generate include files versus compile-able source files, though
that reason escapes me now. Think it had something to do with duplicate symbol
definitions when more than one file includes dasm\_proto.h.

**Special MsBuild Properties:**

`DASMLuaPath` - Defines the absolute filepath to the lua executable used in running dynasm. (By default, this uses the minilua executable included with this repo) 
`DynASMDir` - Defines the absolute filepath to the directory containing dynasm.lua. (By default, this uses the luajit\dynasm included in this repo) If this is not defined, but `DynASMPath` is, it will use the parent directory. This property is required if you want the DynASM headers automatically added to your include search path.
`DynASMPath` - Defines the absolute filepath of the dynasm.lua script. (By default, this uses `$(DynASMDir)\dynasm.lua`, assuming $(DynASMDir) contains a value)
`VsDynASMPath` - Optional property defining the absolute filepath to the vsdynasm executable, which is not used or defined by default. (vsdynasm.exe turned out to be redundant for *reasons*) If this is defined, it will be used to run dynasm. If not, the lua executable and dynasm script path from the properties above will be used.

## Other Stuff

I wrote these customizations while experimenting with the idea of using dynasm
to dynamically generate shellcode for one of my projects. Didn't quite work for
what I needed, but I ended up adding a few extensions to dynasm along the way.
Those additions can be found in the contrib folder in the form of a patch to the
original dynasm.lua script, and the dasm_contrib.lua module. See the file header
of dasm_contrib.lua for details.
