using System;
using System.Collections.Generic;
using System.IO;
using LibGit2Sharp;

namespace mdaiAgent
{
    public class GitService : IDisposable
    {
        private Repository? _repository;
        private string? _repositoryPath;

        public bool IsGitRepository => _repository != null;

        public event EventHandler? GitStatusChanged;

        public void Initialize(string projectPath)
        {
            try
            {
                if (!Repository.IsValid(projectPath))
                {
                    Repository.Init(projectPath);
                }
                _repositoryPath = projectPath;
                _repository = new Repository(projectPath);
                OnGitStatusChanged();
            }
            catch
            {
                _repository = null;
                _repositoryPath = null;
            }
        }

        public List<(string FilePath, string Status)> GetGitStatus()
        {
            var statusList = new List<(string FilePath, string Status)>();
            if (!IsGitRepository || _repository == null) return statusList;

            try
            {
                var status = _repository.RetrieveStatus();
                foreach (var entry in status)
                {
                    string statusText;
                    switch (entry.State)
                    {
                        case FileStatus.NewInWorkdir:
                            statusText = "Untracked";
                            break;
                        case FileStatus.ModifiedInWorkdir:
                            statusText = "Modified";
                            break;
                        case FileStatus.DeletedFromWorkdir:
                            statusText = "Deleted";
                            break;
                        case FileStatus.RenamedInWorkdir:
                            statusText = "Renamed";
                            break;
                        case FileStatus.NewInIndex:
                            statusText = "Staged";
                            break;
                        case FileStatus.ModifiedInIndex:
                            statusText = "Staged Modified";
                            break;
                        case FileStatus.DeletedFromIndex:
                            statusText = "Staged Deleted";
                            break;
                        default:
                            statusText = entry.State.ToString();
                            break;
                    }
                    statusList.Add((entry.FilePath, statusText));
                }
            }
            catch
            {
                // Ignore errors for now
            }
            return statusList;
        }

        public void StageFile(string filePath)
        {
            if (!IsGitRepository || _repository == null) return;
            try
            {
                Commands.Stage(_repository, filePath);
                OnGitStatusChanged();
            }
            catch { }
        }

        public void UnstageFile(string filePath)
        {
            if (!IsGitRepository || _repository == null) return;
            try
            {
                Commands.Unstage(_repository, filePath);
                OnGitStatusChanged();
            }
            catch { }
        }

        public void Commit(string message)
        {
            if (!IsGitRepository || _repository == null) return;
            try
            {
                var signature = GetDefaultSignature();
                if (signature != null)
                {
                    _repository.Commit(message, signature, signature);
                    OnGitStatusChanged();
                }
            }
            catch { }
        }

        public bool SetRemoteUrl(string remoteName, string url)
        {
            if (!IsGitRepository || _repository == null) return false;
            try
            {
                var remote = _repository.Network.Remotes[remoteName];
                if (remote == null)
                {
                    _repository.Network.Remotes.Add(remoteName, url);
                }
                else
                {
                    _repository.Network.Remotes.Update(remoteName, r => r.Url = url);
                }
                return true;
            }
            catch { return false; }
        }

        public string? GetRemoteUrl(string remoteName)
        {
            if (!IsGitRepository || _repository == null) return null;
            return _repository.Network.Remotes[remoteName]?.Url;
        }

        public bool PushToRemote(string remoteName, string branchName, string username, string token)
        {
            if (!IsGitRepository || _repository == null) return false;
            try
            {
                var remote = _repository.Network.Remotes[remoteName];
                if (remote == null) return false;

                var options = new PushOptions
                {
                    CredentialsProvider = (_url, _user, _cred) =>
                        new UsernamePasswordCredentials
                        {
                            Username = username,
                            Password = token
                        }
                };
                
                var branch = _repository.Branches[branchName];
                if (branch == null)
                {
                    // Create branch if it doesn't exist
                    branch = _repository.Branches.Add(branchName, _repository.Head.Tip);
                }

                _repository.Branches.Update(branch,
                    b => b.Remote = remote.Name,
                    b => b.UpstreamBranch = branch.CanonicalName);

                _repository.Network.Push(branch, options);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private Signature? GetDefaultSignature()
        {
            try
            {
                var config = _repository?.Config;
                if (config == null) return null;
                var name = config.Get<string>("user.name")?.Value;
                var email = config.Get<string>("user.email")?.Value;
                if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(email))
                {
                    return new Signature("mdaiAgent User", "user@mdaiagent.local", DateTimeOffset.Now);
                }
                return new Signature(name, email, DateTimeOffset.Now);
            }
            catch
            {
                return new Signature("mdaiAgent User", "user@mdaiagent.local", DateTimeOffset.Now);
            }
        }

        protected virtual void OnGitStatusChanged()
        {
            GitStatusChanged?.Invoke(this, EventArgs.Empty);
        }

        public void Dispose()
        {
            _repository?.Dispose();
        }
    }
}