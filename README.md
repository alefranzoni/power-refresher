<div id="top"></div>

<!-- PROJECT SHIELDS -->
[![Downloads][downloads-shield]][downloads-url]
[![Forks][forks-shield]][forks-url]
[![Stargazers][stars-shield]][stars-url]
[![Issues][issues-shield]][issues-url]
[![Contributors][contributors-shield]][contributors-url]
[![LinkedIn][linkedin-shield]][linkedin-url]
[![Twitter][twitter-shield]][twitter-url]

<!-- LOGO/HEADER -->
<br />
<br />
<div align="center">
  <a href="https://github.com/alefranzoni/power-refresher">
    <img src="images/logo.png" alt="Logo" width="12%">
  </a>

  <h3 align="center">PowerRefresher</h3>

  <p align="center">
    Automate manual updating and publishing of your Power BI Desktop files!
    <br />
    🪲 <a href="https://github.com/alefranzoni/power-refresher/issues">Report Bug</a>
    ·
    🪶 <a href="https://github.com/alefranzoni/power-refresher/issues">Request Feature</a>
  </p>
</div>

<!-- TABLE OF CONTENTS -->
## Contents
  <ol>
    <li>
      <a href="#about-the-project">About The Project</a>
      <ul>
        <li><a href="#built-with">Built With</a></li>
      </ul>
    </li>
    <li>
      <a href="#usage">Usage</a>
      <ul>
        <li><a href="#gui">GUI</a></li>
      </ul>
      <ul>
        <li><a href="#command-line-arguments">Command Line Arguments</a></li>
      </ul>
    </li>
    <li><a href="#roadmap">Roadmap</a></li>
    <li><a href="#license">License</a></li>
    <li><a href="#contact">Contact</a></li>
  </ol>

<!-- ABOUT -->
## About The Project

<img src="images/screenshot.png" alt="screenshot" width="35%">

**Power Refresher** was developed to automate the manual updating and publishing process of Power BI Desktop files. But, how does the application achieve it? Making use of the **UI Automation** library to catch and manipulate the Power BI Application UI controls to do the desired task.

### Built With
* [UI Automation](https://docs.microsoft.com/en-us/dotnet/framework/ui-automation/ui-automation-overview/)

<p align="right">(<a href="#top">↑ Top</a>)</p>

<!-- USAGE -->
## Usage
There are two ways to use this tool, directly from its GUI or by passing command line arguments to the executable. Anyway, if you want a full automated process, I recommend using the second option (command line args).

> **Note**: *Currently, this tool only works with the English version of Power BI Desktop. Anyway, support for Spanish version will be added soon.*

### GUI
Simply, you have to execute the program and set all the options shown in the user interface as you wish, then just press the refresh button to begin the process.

- **Input**
  * Browse/Set: *Press to browse and select the target `*.pbix` file*
  * Timeout: *Set the waiting timeout, in seconds, for file to be opened (max: 600)* <br><br><img src="images/input_file.png" alt="input" width="35%">
- **Options**
  * Refresh all: *Check to refresh all the queries/tables/fields present on the model. If uncheck this option, you have to select which fields want to be updated from the below list.*
  * Publish: *Check to publish your file into a workspace after it has been updated. You must put the target workspace name in the box next to the checkbox control.* 
  * Close file on finish: *Check to close `*.pbix` file on process finish*
  * Close application on finish: *Check to close `PowerRefresher` on process finish. If this option is enabled, when the application is closed, a log file will be created and shown with all the update details* <br><br><img src="images/options_gui.png" alt="options" width="35%">
- **Output**
  * *Just a simple log console that will show you useful information about the process like information messages, errors, and more* <br><br><img src="images/output.png" alt="output" width="35%">
- **Buttons**
  *  Generate Script: *Through this button, you could generate the string to execute the app with command line args, based in your current configuration. Plus, you could generate and save a batch file and just execute it to run the app in a scheduled task, for example*
  *  Refresh: *Start the refresh process* <br><br><img src="images/buttons.png" alt="buttons" width="30%">

<p align="right">(<a href="#top">↑ Top</a>)</p>

### Command Line Arguments
Run the **PowerRefresher** application by passing the following arguments:

```batchfile
-target=path -timeout=180 -refresh_mode=all/fields -fields="[field1,fieldN]" -publish=bool -workspace=name -closefile=bool -closeapp=bool
```
| Command      | Description                            |
|--------------|----------------------------------------|
|`target`      |Full pbix file path                     |
|`timeout`     |Waiting timeout in seconds (max. 600)   |
|`refresh_mode`|Refresh mode, all or selected fields    |
|`fields`      |Fields to update                        |
|`publish`     |True if you want to publish file        |
|`workspace`   |Target workspace name (to publish into) |
|`closefile`   |True if you want to close file on finish|
|`closeapp`    |True if you want to close app on finish |

#### Example
```batchfile
-target="C:\Users\MyUser\Desktop\My File.pbix" -timeout=180 -refresh_mode=fields -fields="[Table1,Table2]" -publish=true -workspace="My workspace" -closefile=true -closeapp=true`
```

> **Note**: *Remember that you can generate the script from the `Generate Script` button.*
<p align="right">(<a href="#top">↑ Top</a>)</p>

<!-- ROADMAP -->
## Roadmap
- [ ] Multi-language Support
    - [ ] Spanish

Check the [open issues](https://github.com/alefranzoni/power-refresher/issues) section for a full list of proposed features (and known issues).
<p align="right">(<a href="#top">↑ Top</a>)</p>

<!-- LICENSE -->
## License
Distributed under the MIT License. See `LICENSE.txt` for more information.
<p align="right">(<a href="#top">↑ Top</a>)</p>

<!-- CONTACT -->
## Contact
Alejandro Franzoni Gimenez - contacto@alejandrofranzoni.com.ar <br><br>
<p align="right">(<a href="#top">↑ Top</a>)</p>

<!-- MD LINKS & IMAGES -->
[contributors-shield]: https://img.shields.io/github/contributors/alefranzoni/power-refresher
[contributors-url]: https://github.com/alefranzoni/power-refresher/graphs/contributors
[forks-shield]: https://img.shields.io/github/forks/alefranzoni/power-refresher
[forks-url]: https://github.com/alefranzoni/power-refresher/network/members
[stars-shield]: https://img.shields.io/github/stars/alefranzoni/power-refresher
[stars-url]: https://github.com/alefranzoni/power-refresher/stargazers
[issues-shield]: https://img.shields.io/github/issues/alefranzoni/power-refresher
[issues-url]: https://github.com/alefranzoni/power-refresher/issues
[downloads-shield]: https://img.shields.io/github/downloads/alefranzoni/power-refresher/total
[downloads-url]: https://github.com/alefranzoni/power-refresher/releases
[linkedin-shield]: https://img.shields.io/badge/-LinkedIn-blue.svg?logo=linkedin
[linkedin-url]: https://linkedin.com/in/alejandrofranzonig
[twitter-shield]: https://img.shields.io/badge/-Twitter-blue.svg?logo=twitter&logoColor=white
[twitter-url]: https://twitter.com/AleFranzoniDGK
[instagram-shield]: https://img.shields.io/badge/-Instagram-blue.svg?logo=instagram&logoColor=white
[instagram-url]: https://www.instagram.com/alefranzoni/
