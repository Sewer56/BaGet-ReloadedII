import * as React from 'react';

class Home extends React.Component<{}, {}> {
    public render() { 
        return ( 
            <div>
                <h1>Reloaded II Community NuGet Repository</h1>
                <text>
                    This is a public NuGet repository for hosting Reloaded-II mods as packages.<br/>
                    Hosted using BaGet, with love, it is used both within the `Download Mods` menu, as seen in the mod launcher and for resolving mod dependencies.<br/><br/>

                    At the current moment in time, the mod loader will only ever install the newest versions of each mod, both as dependency and individually.<br/>
                    The plan is to keep it this way, unless a specific scenario requires it to be any other way.
                </text>

                <h1>Resources</h1>
                <text>
                    At the current moment in time, the repository can sustain about <span className="bold">18GB</span> of packages.
                    There is no set upload limit, however please try to keep your packages to a maximum of 100MB.
                    Realistically this should be enough to last a year or two, depending on Reloaded's rate of adoption.
                </text>

                <h1>Don't be an asshole</h1>
                <text>
                    This repository is open to everyone, registration free.<br/>
                    Anyone can freely upload and unlist packages.<br/><br/>

                    Use common sense. Don't upload random things that aren't mods and try not to tick off certain authors by uploading without their permission.<br/><br/>

                    I'm a student with no active source of income paying out of my own wallet to host this service. 
                    <span className="bold"> If people are unable to behave, I will be forced to restrict uploading to certain users or shut the service down.</span> I have a lot of faith in humanity to be hosting this.
                </text>

                <h1>I am not a web developer</h1>
                <text>
                    This repository is more of a hackjob, in that I have learned the bare minimum of ASP.NET and React from scratch to be able to make basic changes to the site.<br/>
                    This is pretty much a pre-baked solution with some minor modifications here and there.<br/><br/>

                    Lack of registration and personal API keys is due to the inability to compile the official NuGet gallery.
                    I believe it is simply broken with VS2019 at the current moment in time though I have not installed 2017 to confirm.<br/><br/>

                    <span className="bold">"We" (I) am looking for a contributor willing to make/host something better.</span>
                </text>

                <h1>TODO:</h1>
                <ul>
                    <li>Retention Policy: Remove packages at random that aren't latest version when approaching maximum storage capacity.</li>
                    
                </ul>
            </div>
        );
    }
}
 
export default Home;