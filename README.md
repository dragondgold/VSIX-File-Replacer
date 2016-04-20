## File Replacer Extension for Visual Studio Builds

This is an small Python 3 script created to be used as a pre-build event in order to be able to have different files for different build configurations.

## Usage

Imagine we have a project with lots of files, `frontend.js`, `backend.cs`, `myscript.py`, `destroy_the_world.cs` and any other file you want. Now, we have different needs for different customers or build types. `Customer1` has different needs that `Customer2`, but they share a lot of code too, the only thing that changes is `frontend.js`.
So, instead of creating a hole new project duplicating all the files just to build and publish with a different `frontend.js` let's take another approach.

We will create two files called `frontend.Customer1.js` and `frontend.Customer2.js` (note this uses the same syntax used in `Web.config` or `App.config` files in `Visual Studio` projects to handle different build configurations using `xdt:Transform` and `xdt:Locator`, ie: `Web.Release.config`)

Now, on `frontend.Customer1.js` we put all the code for `Customer1` and in `frontend.Customer2.js` all the code from `Customer2`. In our `index.html` we will still be adding `frontend.js`.

Now, create two build configurations named `Customer1` and `Customer2` and build the project with any of this configurations. Before the build process the extension will replace `frontend.js` with `frontend.Customer1.js` or `frontend.Customer2.js` according to the build configuration. On this way, you end up having a different file for every build configuration in an easy way. It's not a perfect solution but it can very useful specially on `Javscript`, `Python` or any other file that can't read settings from the `Web.config` to manage different configurations. Also, when saving `frontend.Customer1.js` or `frontend.Customer2.js`, `frontend.js` will be automatically updated according to the active build configuration, this enables you to modify `.js`, `.css`, `.html` files and reload the browser without needing to rebuild the project.
