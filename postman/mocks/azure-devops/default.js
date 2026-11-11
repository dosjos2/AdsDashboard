const http = require('http');
const me = '11111111-1111-1111-1111-111111111111';
const other = '22222222-2222-2222-2222-222222222222';
let mode = 'normal', identityCalls = 0;
const counts = {};
const date = (days = 0) => new Date(Date.now() - days * 86400000).toISOString();
const columnField='WEF_Team_Kanban.Column', doneField='WEF_Team_Kanban.Column.Done', rowField='WEF_Team_Kanban.Lane';
const boardItem=(id=501)=>({id,rev:1,fields:{'System.Title':`Board item ${id}`,'System.WorkItemType':id===502?'Bug':'Product Backlog Item','System.State':id===501?'New':id===502?'Active':'Committed','System.TeamProject':'Alpha','System.AreaPath':'Alpha\\TeamA','System.IterationPath':'Alpha\\Product\\Sprint 1','System.AssignedTo':{id:id===502?other:me,displayName:id===502?'Other Demo':'Jan Demo',uniqueName:id===502?'other@example.test':'jan.ole.skotterud@helsedir.no'},'System.ChangedDate':date(),[columnField]:id===501?'New':id===502?'In progress':'',[doneField]:id===502,[rowField]:'Expedite','Microsoft.VSTS.Common.Priority':2}});
let boardItems=[boardItem(501),boardItem(502),boardItem(503)], boardPatchAttempts=0;
const boardColumns=[{id:'new',name:'New',columnType:'incoming',stateMappings:{'Product Backlog Item':'New',Bug:'New'}},{id:'active',name:'In progress',columnType:'inProgress',isSplit:true,itemLimit:2,stateMappings:{'Product Backlog Item':'Committed',Bug:'Active'}},{id:'review',name:'Review',columnType:'inProgress',itemLimit:1,stateMappings:{'Product Backlog Item':'Committed',Bug:'Active'}},{id:'done',name:'Done',columnType:'outgoing',stateMappings:{'Product Backlog Item':'Done',Bug:'Closed'}}];
const pr = (id, status, mine = true) => ({
  pullRequestId: id, title: `Synthetic PR ${id}`, status, creationDate: date(3),
  repository: { id: 'repo-a', name: 'sample', project: {name: 'Alpha'} },
  createdBy: { id: mine ? me : other, displayName: mine ? 'Jan Demo' : 'Other Demo' },
  reviewers: [{ id: me, displayName: 'Jan Demo', vote: 0, isRequired: true }, {id: other, displayName: 'Other Demo', vote: 5}],
  isDraft: id === 999, mergeStatus: id === 1 ? 'conflicts' : 'succeeded',
  sourceRefName: 'refs/heads/feature/demo', targetRefName: 'refs/heads/main'
});
http.createServer(async (req, res) => {
  const u = new URL(req.url, 'http://localhost');
  const send = (code, body, headers = {}) => { res.writeHead(code, {'Content-Type':'application/json', ...headers}); res.end(JSON.stringify(body)); };
  const envelope = value => ({count:value.length, value});
  if (u.pathname === '/__control') {
    if (req.method === 'POST') { mode = u.searchParams.get('mode') || 'normal'; identityCalls = 0; if(mode==='board'){boardItems=[boardItem(501),boardItem(502),boardItem(503)];boardPatchAttempts=0;} if(mode==='boardConflict'){boardItems[0].rev++;boardItems[0].fields['System.Title']='Changed externally';} }
    return send(200, {mode, identityCalls, counts, boardPatchAttempts,boardRevision:boardItems[0].rev});
  }
  counts[u.pathname] = (counts[u.pathname] || 0) + 1;
  if (req.headers.authorization !== 'Basic ' + Buffer.from(':test-token').toString('base64')) return send(401, {message:'Invalid test token'});
  const commentRoute = u.pathname.match(/^\/(?:Alpha|project-a)\/_apis\/wit\/workItems\/(\d+)\/comments$/);
  if (commentRoute) {
    if (u.searchParams.get('api-version') !== '7.1-preview.4' || u.searchParams.get('includeDeleted') !== 'false' || u.searchParams.get('$expand') !== 'renderedText') return send(400,{message:'Invalid comment query'});
    const id = Number(commentRoute[1]); const token = u.searchParams.get('continuationToken');
    if (id === 3 || (id === 6 && token)) return send(403,{message:'Discussion forbidden'});
    if (id === 4) return send(200,{value:[]});
    if (id === 2) return send(200,{comments:[],count:0,totalCount:0});
    if (id === 5) return send(200,{comments:[],continuationToken:'repeat'});
    const first = {id:10, createdBy:{displayName:'Anna Demo'},createdDate:date(2/1440),modifiedDate:date(2/1440),text:'@Jan & team',renderedText:'<p>@Jan &amp; team</p>',mentions:[{targetId:me}],isDeleted:false};
    if (token) return send(200,{comments:[first,{commentId:12,createdBy:{displayName:'Per Demo'},createdDate:date(1/1440),modifiedDate:date(1/1440),format:'markdown',text:'Raw **markdown**',mentions:[{targetId:other}]}],count:2,totalCount:3});
    return send(200,{comments:[first,{id:11,isDeleted:true,text:'Deleted'}],count:2,totalCount:3,continuationToken:'comments-page-2'});
  }
  if (u.searchParams.get('api-version') !== '7.1') return send(400, {message:'Expected api-version=7.1'});
  if(u.pathname==='/_apis/projects/project-a/teams') {
    const skip=Number(u.searchParams.get('$skip'));
    return send(200,envelope(skip===0?Array.from({length:100},(_,i)=>({id:'team-'+i,name:'ZZ Team '+i})):[{id:'team-main',name:'Alpha Team'}]));
  }
  const boardPrefix='/Alpha/team-main/_apis/work';
  if(u.pathname===boardPrefix+'/boards') return send(200,envelope([{id:'board-a',name:'Backlog'}]));
  if(u.pathname===boardPrefix+'/boards/board-a') return send(200,{id:'board-a',name:'Backlog',isValid:mode!=='boardInvalid',columns:boardColumns,fields:{columnField:{referenceName:columnField},doneField:{referenceName:mode==='boardMissingField'?'':doneField},rowField:{referenceName:rowField}}});
  if(u.pathname===boardPrefix+'/teamsettings/teamfieldvalues') return send(200,{field:{referenceName:'System.AreaPath'},values:mode==='boardNoAreas'?[]:[{value:'Alpha\\TeamA',includeChildren:true},{value:'Alpha\\Shared',includeChildren:false}]});
  if(u.pathname===boardPrefix+'/teamsettings') return send(200,{backlogIteration:{path:'Alpha\\Product'}});
  const moveRoute=u.pathname.match(/^\/Alpha\/_apis\/wit\/workitems\/(\d+)$/);
  if(moveRoute && req.method==='PATCH') {
    boardPatchAttempts++;
    if(!req.headers['content-type']?.startsWith('application/json-patch+json') || u.searchParams.has('bypassRules') || u.searchParams.has('suppressNotifications'))return send(400,{message:'Invalid patch contract'});
    const patch=req.body||await readBody(req), item=boardItems.find(i=>i.id===Number(moveRoute[1]));
    if(!item)return send(404,{message:'Not found'});
    if(patch[0]?.op!=='test' || patch[0]?.path!=='/rev')return send(400,{message:'Missing revision test'});
    if(patch[0].value!==item.rev)return send(412,{message:'Revision conflict'});
    if(mode==='boardForbidden')return send(403,{message:'Read-only token'});
    if(mode==='boardRules')return send(400,{message:'A required workflow field is missing'});
    if(mode==='boardRateLimit')return send(429,{message:'Rate limited'},{'Retry-After':'1'});
    const allowed=['/fields/System.State','/fields/'+columnField,'/fields/'+doneField];
    if(patch.slice(1).some(p=>p.op!=='add'||!allowed.includes(p.path)))return send(400,{message:'Unexpected field changed'});
    const next=structuredClone(item);
    for(const p of patch.slice(1))next.fields[p.path.slice('/fields/'.length)]=p.value;
    const column=boardColumns.find(c=>c.name===next.fields[columnField]);
    if(!column||column.stateMappings[next.fields['System.WorkItemType']]!==next.fields['System.State'])return send(400,{message:'State and column mismatch'});
    next.rev++;next.fields['System.ChangedDate']=date();boardItems[boardItems.indexOf(item)]=next;
    if(mode==='boardLost'){req.socket.destroy();return;}
    if(mode==='boardUnconfirmed')return send(200,{...next,fields:{...next.fields,[columnField]:'Unexpected column'}});
    return send(200,next);
  }
  if (u.pathname.endsWith('/_apis/identities')) {
    identityCalls++;
    if (mode === 'auth') return send(401, {message:'Expired'});
    if (mode === 'html') {res.writeHead(200, {'Content-Type':'text/html'}); return res.end('<html>Sign in</html>');}
    if (mode === 'retry' && identityCalls === 1) return send(429, {message:'Rate limited'}, {'Retry-After':'1'});
    if (u.searchParams.get('filterValue') !== 'jan.ole.skotterud@helsedir.no') return send(400, {message:'Unexpected email'});
    const identity = {id:me, isActive:true, properties:{Mail:{$value:'jan.ole.skotterud@helsedir.no'}}, providerDisplayName:'Jan Demo'};
    if (mode === 'ambiguous') return send(200, envelope([identity, {...identity, id:other}]));
    return send(200, envelope([identity]));
  }
  if (u.pathname === '/_apis/projects') {
    if (u.searchParams.get('continuationToken') === 'page2') return send(200, envelope([{id:'project-b',name:'Restricted'}]));
    return send(200, envelope([{id:'project-a',name:'Alpha'}]), {'x-ms-continuationtoken':'page2'});
  }
  if (u.pathname === '/_apis/wit/wiql' && req.method === 'POST') {
    const body = req.body || await readBody(req);
    if(body.query?.includes("[System.TeamProject] = 'Alpha'")){
      if(!body.query.includes("[System.AreaPath] UNDER 'Alpha\\TeamA'")||!body.query.includes("[System.AreaPath] = 'Alpha\\Shared'")||!body.query.includes("[System.IterationPath] UNDER 'Alpha\\Product'")||!body.query.includes("'Product Backlog Item'")||!body.query.includes("'Bug'")||body.query.includes("'Task'")||!body.query.includes('@Today - 30')||u.searchParams.get('$top')!=='500')return send(400,{message:'Board query scope is incorrect'});
      const after=Number(body.query.match(/\[System.Id\] > (\d+)/)?.[1]||0);
      const items=mode==='boardMany'?Array.from({length:501},(_,i)=>({id:501+i})):boardItems;
      return send(200,{workItems:items.filter(i=>i.id>after).slice(0,500).map(i=>({id:i.id}))});
    }
    if (body.query?.includes('[System.ChangedDate] >=')) {
      if (u.searchParams.get('$top') !== '500' || u.searchParams.get('timePrecision') !== 'true') return send(400,{message:'Invalid activity scan query'});
      return send(200,{workItems:body.query.includes('[System.Id] > 0') ? [{id:1},{id:3}] : []});
    }
    if (!body.query?.includes("[System.AssignedTo] = 'jan.ole.skotterud@helsedir.no'") || !body.query?.includes("('Task', 'Feature', 'Bug', 'Product Backlog Item')")) return send(400, {message:'Invalid WIQL query'});
    return send(200, {workItems:Array.from({length:201}, (_,i) => ({id:i+1}))});
  }
  if (u.pathname === '/_apis/wit/workitemsbatch' && req.method === 'POST') {
    const body = req.body || await readBody(req);
    if (!Array.isArray(body.ids) || body.ids.length > 200 || body.errorPolicy !== 'Omit') return send(400, {message:'Invalid batch'});
    if(body.ids.some(id=>id>=501))return send(200,envelope(body.ids.map(id=>boardItems.find(i=>i.id===id)||boardItem(id))));
    return send(200, envelope(body.ids.filter(id => id !== 200).map(id => ({id, fields:{
      'System.Title':`Synthetic work ${id}`, 'System.WorkItemType':[5,8].includes(id) ? 'Product Backlog Item' : id % 3 === 0 ? 'Bug' : id % 3 === 1 ? 'Task':'Feature',
      'System.State':id % 4 === 0 ? 'Closed':'Active', 'System.TeamProject':'Alpha', 'System.IterationPath':'Alpha\\Sprint 1',
      ...(id === 1 ? {'System.Description':'<p>First &amp; second</p><p>Next line<br>Final</p>', 'System.AssignedTo':{displayName:'Jan Demo'}, 'System.AreaPath':'Alpha\\Team', 'System.Tags':'UI; Test', 'Microsoft.VSTS.Common.AcceptanceCriteria':'<ul><li>Works</li><li>Verified</li></ul>'} : {}),
      ...(id === 3 ? {'Microsoft.VSTS.TCM.ReproSteps':'<div>Open page</div><div>See error</div>'} : {}),
      'System.ChangedDate':date(10), 'Microsoft.VSTS.Common.Priority':id === 3 ? 1 : 2,
      'Microsoft.VSTS.Common.Severity':id === 3 ? '1 - Critical' : '3 - Medium',
      ...(body.asOf && mode === 'activity' ? {'System.Description':id === 3 || Date.parse(body.asOf) > Date.now()-5*60000 ? `<a data-vss-mention="version:2.0,${me}">@Jan</a> Please review` : 'No mention', 'System.CommentCount':id === 1 ? 1 : 0,'System.ChangedDate':date(2/1440)} : {})
    }}))));
  }
  if (u.pathname.includes('/project-b/')) return send(403, {message:'Restricted project'});
  if (u.pathname === '/project-a/_apis/git/pullrequests') {
    const creator = u.searchParams.get('searchCriteria.creatorId'); const reviewer = u.searchParams.get('searchCriteria.reviewerId');
    if ((creator || reviewer) !== me) return send(400, {message:'Incorrect identity ID'});
    const status = u.searchParams.get('searchCriteria.status');
    if (status !== 'active' && (u.searchParams.get('searchCriteria.queryTimeRangeType') !== 'closed' || !u.searchParams.get('searchCriteria.minTime'))) return send(400, {message:'Missing history cutoff'});
    const skip = Number(u.searchParams.get('$skip'));
    if (status === 'completed') return send(200, envelope([pr(800,status)]));
    if (status === 'abandoned') return send(200, envelope([pr(801,status)]));
    if (reviewer) return send(200, envelope([pr(1,status),pr(999,status,false)]));
    return send(200, envelope(Array.from({length:101}, (_,i) => pr(i+1,'active')).slice(skip,skip+100)));
  }
  if (u.pathname === '/project-a/_apis/build/builds') {
    const build = (id, definition, days, result) => ({id,definition:{id:definition,name:`Pipeline ${definition}`},buildNumber:String(id),status:'completed',result,queueTime:date(days),sourceBranch:'refs/heads/main'});
    if (u.searchParams.get('continuationToken') === 'build2') return send(200,envelope([build(12,2,2,'failed')]));
    return send(200,envelope([build(10,1,0,'succeeded'),build(11,1,1,'failed')]),{'x-ms-continuationtoken':'build2'});
  }
  send(501, {message:'Endpoint not modeled'});
}).listen(process.env.PORT || 4011, '127.0.0.1');
async function readBody(req) { let content=''; for await(const chunk of req) content+=chunk; return content ? JSON.parse(content):{}; }
