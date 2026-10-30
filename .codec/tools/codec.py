#!/usr/bin/env python3
"""Codec history utility. Only writes .codec metadata; never connects to SQL/IIS/Git."""
import argparse, datetime as dt, json, pathlib, re, secrets, sys
ROOT = pathlib.Path(__file__).resolve().parents[2]
META = ROOT / '.codec'

def safe_id(s):
    if not re.fullmatch(r'[A-Z][A-Z0-9]*(?:-[A-Z0-9]+)*', s):
        raise ValueError('FEATURE_ID must be uppercase slug, e.g. CUSTOMER-MANAGEMENT')
    return s

def new(feature, title):
    feature=safe_id(feature)
    now=dt.datetime.now(dt.timezone.utc)
    for attempt in range(100):
        change=f"{feature}-CHG-{now:%Y%m%dT%H%M%SZ}-{secrets.token_hex(3).upper()}"
        if not any((META/d/f'{change}.md').exists() for d in ('changes','reports')) and not any(change == r['change_id'] for h in (META/'features').glob('*/history.json') for r in json.loads(h.read_text(encoding='utf-8'))):
            break
    else:
        raise ValueError('Unable to allocate unique CHANGE_ID')
    fd=META/'features'/feature
    fd.mkdir(parents=True,exist_ok=True)
    (META/'changes').mkdir(exist_ok=True)
    (META/'reports').mkdir(exist_ok=True)
    readme=fd/'README.md'
    if not readme.exists():
        t=(META/'templates'/'feature.md').read_text(encoding='utf-8')
        readme.write_text(t.replace('{{FEATURE_ID}}',feature).replace('{{FEATURE_TITLE}}',title),encoding='utf-8')
    t=(META/'templates'/'change.md').read_text(encoding='utf-8')
    for k,v in {'FEATURE_ID':feature,'CHANGE_ID':change,'CREATED_AT':now.isoformat(),'TITLE':title}.items(): t=t.replace('{{'+k+'}}',v)
    (META/'changes'/f'{change}.md').write_text(t,encoding='utf-8')
    t=(META/'templates'/'report.md').read_text(encoding='utf-8')
    for k,v in {'FEATURE_ID':feature,'CHANGE_ID':change}.items(): t=t.replace('{{'+k+'}}',v)
    (META/'reports'/f'{change}.md').write_text(t,encoding='utf-8')
    history=fd/'history.json'
    records=json.loads(history.read_text(encoding='utf-8')) if history.exists() else []
    records.append({'change_id':change,'created_at':now.isoformat(),'title':title,'status':'PLANNED','commit_sha':None,'migration_status':'NOT_EXECUTED','publish_status':'NOT_PUBLISHED','merge_status':'PENDING_APPROVAL'})
    history.write_text(json.dumps(records,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    with readme.open('a',encoding='utf-8') as f:f.write(f'| `{change}` | {now:%Y-%m-%d} | {title} | — | NOT_EXECUTED | NOT_PUBLISHED | PENDING_APPROVAL |\n')
    print(json.dumps({'feature_id':feature,'change_id':change,'change_file':str((META/'changes'/f'{change}.md').relative_to(ROOT)),'report_file':str((META/'reports'/f'{change}.md').relative_to(ROOT))},ensure_ascii=False,indent=2))

def validate():
    cfg=json.loads((META/'config.json').read_text(encoding='utf-8'))
    errors=[]
    if cfg['database']['create_database_allowed'] or cfg['database']['execute_migrations_allowed'] or cfg['database']['execute_mutating_sql_allowed']: errors.append('Database mutation is not allowed')
    if cfg['deploy']['allow_alternate_target']: errors.append('Alternate publish targets forbidden')
    if cfg['deploy'].get('require_iis_preflight') is not False or cfg['deploy'].get('local_artifact_only') is not True or cfg['deploy'].get('live_iis_overwrite_requires_separate_approval') is not True: errors.append('Local artifact safety guards missing')
    if cfg.get('machine') != {'role':'DEV_TEST','inspect_iis_allowed':False,'probe_server_configuration_allowed':False,'request_administrator_allowed':False,'deploy_iis_allowed':False}: errors.append('DEV/TEST machine policy invalid')
    if cfg['database'].get('read_only_queries_only') is not True or cfg['database'].get('execute_writing_stored_procedures_allowed') is not False or cfg['database'].get('mutating_integration_tests_allowed') is not False or cfg['database'].get('verification_output_default') != 'PRINT_TEXT' or cfg['database'].get('read_query_output') != 'TEXT_UNLESS_GRID_NEEDED' or cfg['database'].get('unavailable_read_status') != 'NOT_VERIFIED': errors.append('Read-only database policy invalid')
    if not cfg['git']['merge_requires_approval']: errors.append('Merge approval disabled')
    if cfg['database']['server'] != r'WIN-HSHL54Q184K\MSSQLSERVER2014' or cfg['database']['name'] != 'TAPortalSys': errors.append('Unexpected SQL server/database')
    if cfg['database'].get('allowed_databases') != ['TAPortalSys']: errors.append('Only TAPortalSys is allowed')
    if cfg['database'].get('required_migration_guard') != "DB_NAME() = 'TAPortalSys'": errors.append('Migration guard missing')
    if not cfg['deploy']['require_approval'] or cfg['git']['force_push_allowed']: errors.append('Approval/force-push policy invalid')
    if cfg['deploy'].get('rename_or_move_target_allowed') is not False: errors.append('Deploy path must be fixed')
    if not cfg['history'].get('append_only') or not cfg['history'].get('read_before_source'): errors.append('History policy disabled')
    for key, expected in {'feature_dir':'.codec/features','change_dir':'.codec/changes','report_dir':'.codec/reports'}.items():
        if cfg['history'][key] != expected: errors.append('Unexpected history directory: '+key)
    seen=set()
    for history in (META/'features').glob('*/history.json'):
        feature=safe_id(history.parent.name)
        for row in json.loads(history.read_text(encoding='utf-8')):
            change=row['change_id']
            if not re.fullmatch(re.escape(feature)+r'-CHG-\d{8}T\d{6}Z-[A-F0-9]{6}',change) or change in seen: errors.append('Invalid/duplicate change: '+change)
            seen.add(change)
            for directory in ('changes','reports'):
                if not (META/directory/f'{change}.md').is_file(): errors.append('Missing '+directory+': '+change)
        if not (history.parent/'README.md').is_file(): errors.append('Missing feature README')
    agents=cfg.get('agents',{})
    if agents.get('applies_to')!='ALL_AI_AGENTS' or agents.get('contract_file')!='AGENTS.md' or agents.get('preserve_foreign_uncommitted_changes') is not True or agents.get('switch_or_create_branch_requires_approval') is not True: errors.append('Multi-agent contract policy invalid')
    if cfg['deploy'].get('republish_changed_projects') is not True or cfg['deploy'].get('artifact_manifest_method')!='SHA256_UTF8_LF_SORTED_RELATIVE_POSIX' or cfg['deploy'].get('artifact_manifest_dir')!='.codec/reports': errors.append('Artifact republish/manifest policy invalid')
    if cfg['deploy'].get('shared_project')!='TAPortal.Shared' or sorted(cfg['deploy'].get('shared_dependents') or [])!=sorted(cfg['deploy']['targets']): errors.append('Shared dependents must cover every web project target')
    report=cfg.get('report',{})
    if [s.split('.')[0] for s in report.get('required_sections',[])]!=list('ABCDEFGHIJKL') or report.get('finding_statuses')!=['FIXED','VERIFIED','OPEN','BLOCKED','PENDING_OPERATOR'] or report.get('separate_source_and_delivery_sha') is not True: errors.append('Unified report structure invalid')
    contract=ROOT/'AGENTS.md'
    if not contract.is_file() or '## MULTI-AGENT CONTRACT' not in contract.read_text(encoding='utf-8'): errors.append('AGENTS.md lacks MULTI-AGENT CONTRACT section')
    missing=[k for k,v in cfg['deploy']['targets'].items() if not v]
    if missing and cfg['deploy'].get('status') != 'PENDING_CONFIGURATION': errors.append('Missing publish paths must remain PENDING_CONFIGURATION')
    for value in cfg['deploy']['targets'].values():
        if value and not pathlib.PureWindowsPath(value).is_absolute(): errors.append('Publish path must be absolute')
    for project, value in cfg['deploy']['targets'].items():
        expected = pathlib.PureWindowsPath(ROOT) / 'src' / 'publish' / ('deployed-' + project)
        if value and pathlib.PureWindowsPath(value) != expected: errors.append('Publish path violates fixed convention: ' + project)
    print('POLICY:', 'PASS' if not errors else 'FAIL')
    print('Publish targets awaiting operator configuration:', ', '.join(missing) or 'none')
    for e in errors:print('ERROR:',e)
    return 1 if errors else 0

def main():
    p=argparse.ArgumentParser(); sub=p.add_subparsers(dest='command',required=True)
    n=sub.add_parser('new');n.add_argument('--feature',required=True);n.add_argument('--title',required=True)
    sub.add_parser('list');sub.add_parser('validate')
    a=p.parse_args()
    if a.command=='new':new(a.feature,a.title)
    elif a.command=='list':
        for f in sorted((META/'features').glob('*/history.json')):
            print(f.parent.name)
            for row in json.loads(f.read_text(encoding='utf-8')):print(' ',row['change_id'],row['status'])
    elif a.command=='validate':return validate()
    return 0
if __name__=='__main__':
    try:sys.exit(main())
    except (ValueError,FileNotFoundError,KeyError) as e:print('ERROR:',e,file=sys.stderr);sys.exit(2)
