import { Icon } from 'office-ui-fabric-react/lib/Icon';
import * as React from 'react';
import { Link } from 'react-router-dom';
import './SearchResults.css';

interface ISearchResultsProps {
  input: string;
}

interface IPackage {
  id: string;
  authors: string;
  totalDownloads: number;
  version: string;
  tags: string[];
  description: string;
  iconUrl: string;
}

interface ISearchResultsState {
  includePrerelease: boolean;
  packageType: string;
  targetFramework: string;
  items: IPackage[];
}

interface ISearchResponse {
  data: IPackage[];
}

class SearchResults extends React.Component<ISearchResultsProps, ISearchResultsState> {

  private readonly defaultIconUrl: string = 'https://www.nuget.org/Content/gallery/img/default-package-icon-256x256.png';
  private resultsController?: AbortController;

  constructor(props: ISearchResultsProps) {
    super(props);

    this.state = {
      includePrerelease: true,
      items: [],
      packageType: 'any',
      targetFramework: 'any'
    };
  }

  public componentDidMount() {
    this._loadItems(
      this.props.input,
      this.state.includePrerelease,
      this.state.packageType,
      this.state.targetFramework);
  }

  public componentWillUnmount() {
    if (this.resultsController) {
      this.resultsController.abort();
    }
  }

  public componentWillReceiveProps(props: Readonly<ISearchResultsProps>) {
    if (props.input === this.props.input) {
      return;
    }

    this._loadItems(
      props.input,
      this.state.includePrerelease,
      this.state.packageType,
      this.state.targetFramework);
  }

  public render() {
    return (
      <div>
        {this.state.items.map(value => (
          <div key={value.id} className="row search-result">
            <div className="col-sm-1 hidden-xs hidden-sm">
              <img src={value.iconUrl || this.defaultIconUrl} className="package-icon img-responsive" onError={this.loadDefaultIcon} />
            </div>
            <div className="col-sm-11">
              <div>
                <Link to={`/packages/${value.id}`} className="package-title">{value.id}</Link>
                <span>by: {value.authors}</span>
              </div>
              <ul className="info">
                <li>
                  <span>
                    <Icon iconName="Download" className="ms-Icon" />
                    {value.totalDownloads.toLocaleString()} total downloads
                  </span>
                </li>
                <li>
                  <span>
                    <Icon iconName="Flag" className="ms-Icon" />
                    Latest version: {value.version}
                  </span>
                </li>
                <li>
                  <span className="tags">
                    <Icon iconName="Tag" className="ms-Icon" />
                    {value.tags.join(' ')}
                  </span>
                </li>
              </ul>
              <div>
                {value.description}
              </div>
            </div>
          </div>
        ))}
      </div>
    );
  }

  private _loadItems(query: string, includePrerelease: boolean, packageType: string, targetFramework: string): void {
    if (this.resultsController) {
      this.resultsController.abort();
    }

    this.resultsController = new AbortController();

    this.setState({
      includePrerelease,
      items: [],
      packageType,
      targetFramework,
    });

    const url = this.buildUrl(query, includePrerelease, packageType, targetFramework);

    fetch(url, {signal: this.resultsController.signal}).then(response => {
      return response.json();
    }).then(resultsJson => {
      const results = resultsJson as ISearchResponse;

      this.setState({
        includePrerelease,
        items: results.data,
        targetFramework,
      });
    });
  }

  private buildUrl(query: string, includePrerelease: boolean, packageType?: string, targetFramework?: string) {
    const parameters: { [parameter: string]: string } = {
      semVerLevel: "2.0.0"
    };

    if (query && query.length !== 0) {
      parameters.q = query;
    }

    if (includePrerelease) {
      parameters.prerelease = 'true';
    }

    if (packageType && packageType !== 'any') {
      parameters.packageType = packageType;
    }

    if (targetFramework && targetFramework !== 'any') {
      parameters.framework = targetFramework;
    }

    const queryString = Object.keys(parameters)
      .map(k => `${k}=${encodeURIComponent(parameters[k])}`)
      .join('&');

    return `/v3/search?${queryString}`;
  }

  private loadDefaultIcon = (e: React.SyntheticEvent<HTMLImageElement>) => {
    e.currentTarget.src = this.defaultIconUrl;
  }
}

export default SearchResults;
