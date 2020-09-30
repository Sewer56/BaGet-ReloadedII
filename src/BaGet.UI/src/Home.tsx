import * as React from 'react';
import mainScreen from './assets/mainScreen.png';

class Home extends React.Component<{}, {}> {
    public render() { 
        return ( 
            <p>
                <h1>Reloaded II Community NuGet Repository</h1>
                <text>
                    This is a public NuGet repository for hosting Reloaded-II code mods as packages.<br/>
                    Hosted using BaGet 🥖, it's the default repository available in the `Download Mods` menu used for resolving mod dependencies.
                </text>

                <div>
                    <br/>
                    <img src={mainScreen} />
                </div>

                <h1>Resources</h1>
                <text>
                    At the current moment in time, the repository can sustain about <span className="bold">18GB</span> of packages.<br/>
                    There is no set upload limit, however please try to keep your packages to a maximum of 50MB.
                </text>

                <h1>Please be nice!</h1>
                <text>
                    This repository is open to everyone, registration free.<br/>
                    Anyone can freely download, upload and delete (their own) packages.<br/><br/>

                    Use common sense. Please don't upload non-mod content or packages without the original authors' permission.<br/><br/>
                    
                    <span className="bold"> If people are unable to behave, I will be forced to make this service read-only.</span><br/>
                    I have a lot of faith in humanity to be hosting this.
                </text>

                <h1>I am not a web developer</h1>
                <text>
                    This repository is more of a hackjob, in that I have learned the bare minimum of ASP.NET and React from scratch to be able to make basic changes to the site.<br/>
                    Themed some stuff, implemented an authentication system and a storage reporting feature. Aside from that, this is pretty much a pre-baked solution.<br/><br/>
                </text>
            </p>
        );
    }
}
 
export default Home;