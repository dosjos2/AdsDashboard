const fs=require('fs'),path=require('path');
const root=path.resolve(__dirname,'../postman/collections/board');
const requests={
 'list Project Teams.request.yaml':{url:'{{baseUrl}}/_apis/projects/project-a/teams?api-version=7.1&$top=100&$skip=0',test:"pm.test('Teams envelope',()=>pm.expect(pm.response.json().value).to.be.an('array'));"},
 'list Team Boards.request.yaml':{url:'{{baseUrl}}/Alpha/team-main/_apis/work/boards?api-version=7.1',test:"pm.test('Board references',()=>pm.expect(pm.response.json().value[0].id).to.equal('board-a'));"},
 'read Board Configuration.request.yaml':{url:'{{baseUrl}}/Alpha/team-main/_apis/work/boards/board-a?api-version=7.1',test:"pm.test('Board columns and specific field',()=>{pm.expect(pm.response.json().columns).to.have.length(4);pm.expect(pm.response.json().fields.columnField.referenceName).to.equal('WEF_Team_Kanban.Column');});"},
 'read Team Areas.request.yaml':{url:'{{baseUrl}}/Alpha/team-main/_apis/work/teamsettings/teamfieldvalues?api-version=7.1',test:"pm.test('Team area scope',()=>pm.expect(pm.response.json().values[0].includeChildren).to.equal(true));"},
 'read Backlog Iteration.request.yaml':{url:'{{baseUrl}}/Alpha/team-main/_apis/work/teamsettings?api-version=7.1',test:"pm.test('Backlog iteration',()=>pm.expect(pm.response.json().backlogIteration.path).to.equal('Alpha\\\\Product'));"},
 'move Board Card.request.yaml':{url:'{{baseUrl}}/Alpha/_apis/wit/workitems/501?api-version=7.1',body:[{op:'test',path:'/rev',value:1},{op:'add',path:'/fields/System.State',value:'Committed'},{op:'add',path:'/fields/WEF_Team_Kanban.Column',value:'In progress'},{op:'add',path:'/fields/WEF_Team_Kanban.Column.Done',value:false}],test:"pm.test('Move is confirmed',()=>{pm.expect(pm.response.json().rev).to.equal(2);pm.expect(pm.response.json().fields['WEF_Team_Kanban.Column']).to.equal('In progress');});"}
};
function walk(dir){for(const entry of fs.readdirSync(dir,{withFileTypes:true})){if(entry.name==='.resources')continue;const file=path.join(dir,entry.name);if(entry.isDirectory()){walk(file);continue;}const r=requests[entry.name];if(!r)continue;const body=r.body?`body:\n  type: json\n  content: ${JSON.stringify(JSON.stringify(r.body))}\n`:'';fs.writeFileSync(file,`$kind: http-request\nname: ${JSON.stringify(entry.name.replace('.request.yaml',''))}\norder: ${Object.keys(requests).indexOf(entry.name)*1000+1000}\nmethod: ${r.body?'PATCH':'GET'}\nurl: ${JSON.stringify(r.url)}\nheaders:\n  - {key: Accept, value: application/json}\n  - {key: Content-Type, value: '${r.body?'application/json-patch+json':'application/json'}'}\n${body}scripts:\n  - type: afterResponse\n    language: text/javascript\n    code: ${JSON.stringify(r.test)}\n`);}}
walk(root);
fs.writeFileSync(path.join(root,'.resources/definition.yaml'),`$kind: collection
name: Board contract checks - synthetic local server only
description: Generated from board.yaml. Deliberately restricted to the local simulation because one request writes a synthetic work item.
variables:
  - {key: baseUrl, value: 'http://127.0.0.1:4010/azure'}
  - {key: basicAuthPassword, value: 'test-token'}
auth:
  type: basic
  credentials:
    - {key: username, value: ''}
    - {key: password, value: '{{basicAuthPassword}}'}
scripts:
  - type: http:beforeRequest
    language: text/javascript
    code: |
      if (pm.variables.get('baseUrl') !== 'http://127.0.0.1:4010/azure') throw new Error('These write checks are restricted to the local synthetic simulation.');
  - type: http:afterResponse
    language: text/javascript
    code: |
      pm.test('HTTP 200',()=>pm.response.to.have.status(200));
      pm.test('JSON response',()=>pm.expect(pm.response.json()).to.be.an('object'));
`);
