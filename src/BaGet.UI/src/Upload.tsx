import { Icon } from 'office-ui-fabric-react/lib/Icon';
import * as React from 'react';
import * as CopyToClipboard from 'react-copy-to-clipboard';

import './Upload.css';

interface IUploadState {
  selected: Tab;
  content: string[];
  documentationUrl: string;
  name: string;
  storageSpace: IStorageSpace
}

interface IStorageSpace {
  bytesConsumed: number;
  totalBytes: number;
}

enum Tab {
  DotNet,
  NuGet,
  Paket,
  PowerShellGet
}

class Upload extends React.Component<{}, IUploadState> {

  private baseUrl: string;
  private serviceIndexUrl: string;
  private publishUrl: string;

  constructor(props: {}) {
    super(props);

    const pathEnd = window.location.href.indexOf("/upload");

    this.baseUrl = window.location.href.substring(0, pathEnd);
    this.serviceIndexUrl = this.baseUrl + "/v3/index.json";
    this.publishUrl = this.baseUrl + "/api/v2/package";
    this.state = this.buildState(Tab.DotNet);
  }

  public componentDidMount() {
    const storageUrl = `/custom/storage/storage.json`;

    fetch(storageUrl, {}).then(response => {
      return response.json();
    }).then(json => {
      const storageInfo = json as IStorageSpace;
      this.setState({ storageSpace: storageInfo });
    }).catch(
      (e) => {
        // tslint:disable-next-line:no-console
        console.log("Failed to get storage info.", e)}
    );
  }

  public render() {
    const percentageStorageConsumed = (this.state.storageSpace.bytesConsumed / this.state.storageSpace.totalBytes) * 100;
    const percentageStorageConsumedString = percentageStorageConsumed + "%";

    return (
      <div className="col-sm-12">
        <h1>Storage Usage</h1>
        
        <div className="progress">
          <div className="progress-bar progress-bar-danger" 
               role="progressbar"
               style={{width: percentageStorageConsumedString}}>
            {this.toGigabytes(this.state.storageSpace.bytesConsumed)}GB / {this.toGigabytes(this.state.storageSpace.totalBytes)}GB
          </div>
        </div>

        <h1>Rules of Uploading</h1>

        <text>
          This NuGet service associates individual packages with a user supplied "API" key which must be specified.
          An API key can be any string/piece of text of arbitrary length between 1 and 128 characters (your pick!). <br/><br/>

          Once a package is uploaded, it is forever associated with that key. In order to update the package and/or delete the package from the service, you will need to use the same key that originally uploaded the package. Using a different key will return 401 (Unauthorized).
        </text>

        <h1>Recommendations</h1>
        <ul>
            <li>Please save your keys somewhere in an accessible location.</li>
            <li>Please don't lose your key; currently no mechanism exists to reset keys.</li>
        </ul>

        <text className="bold">
          Please do not use your password as the API key.<br/>
          The API keys are stored insecurely in the database, as plain text.
        </text>

        <h1>How to Upload</h1>
        <hr className="breadcrumb-divider" />

        <div>You can push packages using the service index <code>{this.serviceIndexUrl}</code>.</div>

        <div className="upload-info">
          <ul className="nav">
            <UploadTab type={Tab.DotNet} selected={this.state.selected} onSelect={this.handleSelect} />
            <UploadTab type={Tab.NuGet} selected={this.state.selected} onSelect={this.handleSelect} />
            <UploadTab type={Tab.Paket} selected={this.state.selected} onSelect={this.handleSelect} />
            <UploadTab type={Tab.PowerShellGet} selected={this.state.selected} onSelect={this.handleSelect} />
          </ul>

          <div className="content">
            <div className="script">
              {this.state.content.map(value => (
                <div key={value}>
                    {value}
                </div>
              ))}
            </div>
            <div className="copy-button">
              <CopyToClipboard text={this.state.content.join("\n")}>
                <button className="btn btn-default btn-warning" type="button" data-tottle="popover" data-placement="bottom" data-content="Copied">
                  <Icon iconName="Copy" className="ms-Icon" />
                </button>
              </CopyToClipboard>
            </div>
          </div>
          <div className="icon-text alert alert-warning">
            For more information, please refer to <a target="_blank" href={this.state.documentationUrl}>{this.state.name}'s documentation</a>.
          </div>
        </div>
      </div>
    );
  }

  private toGigabytes = (bytes: number) => {
    return (bytes / 1000 / 1000 / 1000).toFixed(2);
  }

  private handleSelect = (selected: Tab) =>
    this.setState(this.buildState(selected));

  private buildState(tab: Tab): IUploadState {
    let name: string;
    let content: string[];
    let documentationUrl: string;

    switch (tab) {
      case Tab.DotNet:
        name = ".NET CLI";
        content = [`dotnet nuget push -s ${this.serviceIndexUrl} -k YOUR-UNIQUE-KEY package.nupkg`];
        documentationUrl = "https://docs.microsoft.com/en-us/dotnet/core/tools/dotnet-nuget-push";
        break;

      case Tab.NuGet:
        name = "NuGet";
        content = [`nuget -Source ${this.serviceIndexUrl} -ApiKey YOUR-UNIQUE-KEY package.nupkg`];
        documentationUrl = "https://docs.microsoft.com/en-us/nuget/tools/cli-ref-push";
        break;

      case Tab.Paket:
        name = "Paket";
        content = [`paket push --url ${this.baseUrl} --api-key YOUR-UNIQUE-KEY package.nupkg`];
        documentationUrl = "https://fsprojects.github.io/Paket/paket-push.html";
        break;

      default:
      case Tab.PowerShellGet:
        name = "PowerShellGet";
        content = [
          `Register-PSRepository -Name "BaGet" -SourceLocation "${this.serviceIndexUrl}" -PublishLocation "${this.publishUrl}" -NuGetApiKey YOUR-UNIQUE-KEY -InstallationPolicy "Trusted"`,
          `Publish-Module -Name PS-Module -Repository BaGet`
        ];
        documentationUrl = "https://docs.microsoft.com/en-us/powershell/module/powershellget/publish-module";
        break;
    }

    return {
      content,
      documentationUrl,
      name,
      selected: tab,
      storageSpace: { bytesConsumed: 0, totalBytes: 0 }
    };
  }
}

interface IUploadTabProps {
  type: Tab;
  selected: Tab;
  onSelect(value: Tab): void;
}

// tslint:disable-next-line:max-classes-per-file
class UploadTab extends React.Component<IUploadTabProps> {

  private title: string;

  constructor(props: IUploadTabProps) {
    super(props);

    switch (props.type) {
      case Tab.DotNet: this.title = ".NET CLI"; break;
      case Tab.NuGet: this.title = "NuGet CLI"; break;
      case Tab.Paket: this.title = "Paket CLI"; break;
      case Tab.PowerShellGet: this.title = "PowerShellGet"; break;
    }
  }

  public render() {
    if (this.props.type === this.props.selected) {
      return <li className="active"><a href="#">{this.title}</a></li>
    }

    return <li><a href="#" onClick={this.onSelect}>{this.title}</a></li>
  }

  private onSelect = (e: React.MouseEvent<HTMLAnchorElement>) => this.props.onSelect(this.props.type);
}

export default Upload;
