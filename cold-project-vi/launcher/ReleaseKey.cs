// The release keys' public halves: the launcher installs an update only when manifest.json is signed
// with one of their private halves. Two people publish this launcher: edupode (edupode/cold-project-vi)
// and felicienradio (felicienradio/cold-project-vi); each private key never leaves its owner's PC
// (scripts\new-release-key.ps1 made them). A manifest's files must still come from the repository the
// launcher updates from (Updater.DownloadPrefix).
namespace VIctoryLauncher
{
    static class ReleaseKey
    {
        // edupode's.
        public const string PublicXml = "<RSAKeyValue><Modulus>42dDIOMzxtofDge6A4ZfY66fmb/k5TYrK+s4RkvWwlrdudsU81xADauQe+xn6QaebyNCXG4NM1e26yHy/Jdacb7y3ZReqkx4Sh7qX9Hui4rSyxUKG2tJsYCfOFPOMK1QgbUfnqQ6vJJLykWAQUbEVOtZxW89CVjUBQUzspjE3/j/p6WyDYO+PpN6OieIjpgQ7rDQSJ9lSe0krDZHYWqhqfsaL/2kGZPeERrBnaa7XrAgxru9/8VAZ5fOUorXY346N82vnb+Jv8sxeL/FYwCPWd4A38cMOTufN68GFnORIMBuxMYDTnVJ4qtc6erWqARdcVm5yrNHQM5YzeUlLEXY92gLAYa7RXoYXo7Ta459uhxzeT9shG+gX4FQK+6+ObOj0a4FSNCTFIa+XmTN1M/FHrDVwzDaO5YAtd0Et4tktFnm4F21Szg2rjOE7R2l+nK4fAkA4Ik0S0BGff5HTMip9whwucbJCiIV+Nmfy1izWM1yFhK3y3ifcSS/Z+YZMBiB</Modulus><Exponent>AQAB</Exponent></RSAKeyValue>";
        // felicienradio's.
        public const string FelicienradioXml = "<RSAKeyValue><Modulus>yt4Zt0Sed9Cu8T4BXatkqgeiD+zSd37fJilTOGmaCi2+Gl7Dguar0nJ6Op4YiVmzIMNee13YUwq88YisibAdWdev99MzZMh3nyXb1fA4y4lVX/dpY8iSJjWIEIoDesw0tH61eU//TtFtkFkNbxgQ+/OvtfkSCjdjTBJy4O27/vb039/+TT6zlApeQm2jbu7C/ATgNWloPfihADNkUgxCQU6MtKAClQ47ozo4szS/fonVgG0nyPfLvwhBjD5zyTAzypNXuAtvhHIJ6BCrJacQy/MDDw56eFDaQK9A8Fslu+gWO7+pkjFlGLYDEPtXBBqGNx6hfFeDqD+aFgsdeXdlLNlg8OkEQITaeRdfsUBNeEF1MdZEOYMNSy8NSGNKowAhHA6SoCLaWWl1Vd+ZuBmguN9dHXJEpIwiVSuHedyTBvPUh69TCHLTaYepXBVEC0MRwESHaRyPV8BVWxQrrd5UKqEQDuXNXPVi4L4HUUOUqaFq/VFeXayAd+EC5TPL3oPp</Modulus><Exponent>AQAB</Exponent></RSAKeyValue>";
        public static readonly string[] All = { PublicXml, FelicienradioXml };
    }
}
