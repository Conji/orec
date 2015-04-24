Ore Compiler (orec)
===================
NOTE: Here's the README. Was listening to depressing Blink-182 while writing, so yeah.

Orec is used to compile the plugins that devs use for Syhno. It's also used for uploading to the site (soon&trade;).
To compile a plugin, 
```
cd %directory_of_the_.csproj_file%
```
```
orec 
```
It'll install the ore in your local machine as well as compress all resources into `.filebuf`. `.ore` is the file that 
orec uses to read the ore's data. 
Args:
----
```
--restart
```
Restarts the current plugin data. Will overwrite the .ore file and .filebuf.
```
--project
```
To be run like `--project=YourProject.csproj`. If you have multiple projects in the folder (hey, some people are weird),
you can run this to specify which project to build.
```
--pack-only
```
This will pack the plugin, but will not install on the local machine. Useful if you're packing for debugging purposes only.
soon&trade;
```
--upload
```
This will upload the ore at the end of the packing process. If no valid credentials are found on the machine, it will
not upload and ask for you to visit http://ore.syhno.net/account/guids to set your machines GUID as a valid machine.
