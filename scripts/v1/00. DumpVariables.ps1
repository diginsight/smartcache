Get-Module | Remove-Module
$keys = @('PSBoundParameters','PWD','*Preference') + $PSBoundParameters.Keys 
Get-Variable -Exclude $keys | Remove-Variable -EA 0

$projectBaseName = $($env:PROJECTBASENAME)
if ([string]::IsNullOrEmpty($projectBaseName)) { $projectBaseName = "101.Samples" }

$scriptFolder = $($PSScriptRoot.TrimEnd('\'));
Import-Module "$scriptFolder\Common.ps1"

Set-Location $scriptFolder
$scriptName = $MyInvocation.MyCommand.Name
Start-Transcript -Path "\Logs\$scriptName.log" -Append

    Write-Host "Build variables '$($env:SOLUTION)'  
    projectBaseName: $($env:PROJECTBASENAME)
    solution: $($env:SOLUTION)
    packageFolder: $($env:PACKAGEFOLDER)
    commonDiagnosticProj: $($env:COMMONDIAGNOSTICPROJ)
    buildPlatform: $($env:BUILDPLATFORM)
    buildConfiguration: $($env:BUILDCONFIGURATION)
    _
    Build.BuildNumber: $($env:BUILD_BUILDNUMBER)
    Build.BuildId: $($env:BUILD_BUILDID)
    ------------
    System.Debug  
    ------------
    Agent.BuildDirectory: $($env:AGENT_BUILDDIRECTORY)
    Agent.HomeDirectory: $($env:AGENT_HOMEDIRECTORY)
    Agent.Id: $($env:AGENT_ID)
    Agent.JobName: $($env:AGENT_JOBNAME)
    Agent.JobStatus: $($env:AGENT_JOBSTATUS)
    Agent.MachineName: $($env:AGENT_MACHINENAME)
    Agent.Name: $($env:AGENT_NAME)
    Agent.OS: $($env:AGENT_OS)
    Agent.OSArchitecture: $($env:AGENT_OSARCHITECTURE)
    Agent.TempDirectory: $($env:AGENT_TEMPDIRECTORY)
    Agent.ToolsDirectory: $($env:AGENT_TOOLSDIRECTORY)
    Agent.WorkFolder: $($env:AGENT_WORKFOLDER)
    ------------------
    Pipeline variables
    ------------------
    Pipeline.Workspace: $($env:PIPELINE_WORKSPACE)
    ------------------------
    Deployment job variables
    ------------------------
    Environment.Name: ___
    Environment.Id: ___
    Environment.ResourceName: ___
    Environment.ResourceId: ___
    ---------------
    Build variables
    ---------------
    Build.ArtifactStagingDirectory: $($env:BUILD_ARTIFACTSTAGINGDIRECTORY)
    Build.BuildId: $($env:BUILD_BUILDID)
    Build.BuildNumber: $($env:BUILD_BUILDNUMBER)
    Build.BuildUri: $($env:BUILD_BUILDURI)
    Build.BinariesDirectory: $($env:BUILD_BINARIESDIRECTORY)
    Build.ContainerId: $($env:BUILD_CONTAINERID)
    Build.DefinitionName: $($env:BUILD_DEFINITIONNAME)
    Build.DefinitionVersion: $($env:BUILD_DEFINITIONVERSION)
    Build.QueuedBy: $($env:BUILD_QUEUEDBY)
    Build.Reason: $($env:BUILD_REASON)
    Build.Repository.Clean: $($env:BUILD_REPOSITORY_CLEAN)
    Build.Repository.LocalPath: $($env:BUILD_REPOSITORY_LOCALPATH)
    Build.Repository.ID: $($env:BUILD_REPOSITORY_ID)
    Build.Repository.Name: $($env:BUILD_REPOSITORY_NAME)
    Build.Repository.Provider: $($env:BUILD_REPOSITORY_PROVIDER)
    Build.Repository.Uri: $($env:BUILD_REPOSITORY_URI)
    Build.RequestedFor: $($env:BUILD_REQUESTEDFOR)
    Build.RequestedForEmail: $($env:BUILD_REQUESTEDFOREMAIL)
    Build.RequestedForId: $($env:BUILD_REQUESTEDFORID)
    Build.SourceBranch: $($env:BUILD_SOURCEBRANCH)
    Build.SourceBranchName: $($env:BUILD_SOURCEBRANCHNAME)
    Build.SourcesDirectory: $($env:BUILD_SOURCESDIRECTORY)
    Build.SourceVersion: $($env:BUILD_SOURCEVERSION)
    Build.SourceVersionMessage: $($env:BUILD_SOURCEVERSIONMESSAGE)
    Build.StagingDirectory: $($env:BUILD_STAGINGDIRECTORY)
    Build.Repository.Git.SubmoduleCheckout: $($env:BUILD_REPOSITORY_GIT_SUBMODULECHECKOUT)
    Build.TriggeredBy.BuildId: ___
    Build.TriggeredBy.DefinitionId: ___
    Build.TriggeredBy.DefinitionName: ___
    Build.TriggeredBy.BuildNumber: ___
    Build.TriggeredBy.ProjectID: ___
    Common.TestResultsDirectory: $($env:COMMON_TESTRESULTSDIRECTORY)
    ----------------
    System variables
    ----------------
    System.AccessToken: $($env:SYSTEM_ACCESSTOKEN)
    System.CollectionId: $($env:SYSTEM_COLLECTIONID)
    System.CollectionUri: $($env:SYSTEM_COLLECTIONURI)
    System.DefaultWorkingDirectory: $($env:SYSTEM_DEFAULTWORKINGDIRECTORY)
    System.DefinitionId: $($env:SYSTEM_DEFINITIONID)
    System.HostType: $($env:SYSTEM_HOSTTYPE)
    System.JobAttempt: $($env:SYSTEM_JOBATTEMPT)
    System.JobDisplayName: $($env:SYSTEM_JOBDISPLAYNAME)
    System.JobId: $($env:SYSTEM_JOBID)
    System.JobName: $($env:SYSTEM_JOBNAME)
    System.PhaseAttempt: $($env:SYSTEM_PHASEATTEMPT)
    System.PhaseDisplayName: $($env:SYSTEM_PHASEDISPLAYNAME)
    System.PhaseName: $($env:SYSTEM_PHASENAME)
    System.StageAttempt: $($env:SYSTEM_STAGEATTEMPT)
    System.StageDisplayName: $($env:SYSTEM_STAGEDISPLAYNAME)
    System.StageName: $($env:SYSTEM_STAGENAME)
    System.PullRequest.IsFork: ___
    System.PullRequest.PullRequestId: ___
    System.PullRequest.PullRequestNumber: ___
    System.PullRequest.SourceBranch: ___
    System.PullRequest.TargetBranch: ___
    System.TeamFoundationCollectionUri: $($env:SYSTEM_TEAMFOUNDATIONCOLLECTIONURI)
    System.TeamProject: $($env:SYSTEM_TEAMPROJECT)
    System.TeamProjectId: $($env:SYSTEM_TEAMPROJECTID)
    ----------------"

    # set | Out-Default
    # set

    Write-Host ""
    Write-Host "Environment variables"

    $var = (gci env:*).GetEnumerator() | Sort-Object Name
    $out = ""
    Foreach ($v in $var) {
        $out = $out + "`t{0,-28} = {1,-28}`n" -f $v.Name, $v.Value
    }

    Write-Host $out
     
    dir
    # write-output "dump variables on $env:BUILD_ARTIFACTSTAGINGDIRECTORY\test.md"
    # $fileName = "$env:BUILD_ARTIFACTSTAGINGDIRECTORY\test.md"
    # set-content $fileName $out
    # write-output "##vso[task.addattachment type=Distributedtask.Core.Summary;name=Environment Variables;]$fileName"

Stop-Transcript

