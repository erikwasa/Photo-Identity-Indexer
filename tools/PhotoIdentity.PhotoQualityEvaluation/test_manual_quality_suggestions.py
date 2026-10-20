"""Synthetic-only tests for low-quality manual suggestions and local reviewer safety."""
import copy
import hashlib
import json
from pathlib import Path
import tempfile
import threading
import unittest

import manual_quality_suggestions as s
import manual_quality_suggestion_review as review

IDS=[f"00000000-0000-0000-0000-{n:012d}" for n in range(1,5)]


def fixture():
    profiles=[
        ["candidate-low-sharpness"],
        ["candidate-severe-underexposure","candidate-shadow-clipping"],
        [],
        ["candidate-low-contrast"],
    ]
    rows=[]
    for i,reasons in enumerate(profiles):
        body=f"synthetic-proxy-{i}".encode()
        rows.append({
            "revisionId": IDS[i], "sourceKey": f"private-source-{i}",
            "proxyRelativePath": f"{i}.jpg",
            "proxySha256": hashlib.sha256(body).hexdigest(),
            "analysisMilliseconds": .1,
            "evidence": {"algorithmVersion":s.INPUT_ALGORITHM,
                         "proxyProfileId":s.EXPECTED_PROFILE,
                         "reasons":reasons},
            "review": {"category":None,"verdict":None,
                       "personallyImportant":None,"notes":None},
        })
    return {"schemaVersion":1,"algorithmVersion":s.INPUT_ALGORITHM,
            "photoCount":len(rows),"photos":rows}


def local_session(folder):
    root=Path(folder)
    private=root/"private";private.mkdir()
    proxy=root/"proxies";proxy.mkdir()
    for i in range(4):
        (proxy/f"{i}.jpg").write_bytes(f"synthetic-proxy-{i}".encode())
    manifest,report=s.build(fixture())
    source=private/"manifest.json";state=private/"decisions.json"
    source.write_text(json.dumps(manifest),encoding="utf-8")
    session=review.session_load(source,state,proxy,"http://127.0.0.1:5080")
    return session,report,manifest


class ManualQualitySuggestionTests(unittest.TestCase):
    def test_ranked_candidate_generation_and_true_abstention(self):
        manifest,report=s.build(fixture())
        self.assertEqual(report["sourcePhotos"],4)
        self.assertEqual(report["candidatePhotos"],3)
        self.assertEqual(manifest["candidates"][0]["revisionId"],IDS[1])
        self.assertEqual(manifest["candidates"][1]["revisionId"],IDS[0])
        self.assertTrue(manifest["noAutomaticExclusion"])
        self.assertTrue(manifest["noCollectionChanges"])
        self.assertNotIn("sourceKey",json.dumps(manifest))
        self.assertEqual(s.validate_manifest(manifest),set([IDS[0],IDS[1],IDS[3]]))

    def test_source_human_label_or_unknown_reason_is_rejected(self):
        for modification in (
            lambda f:f["photos"][0]["review"].update({"verdict":"technical-failure"}),
            lambda f:f["photos"][0]["evidence"].update(
                {"reasons":["candidate-model-secret"]}),
            lambda f:f["photos"][0].update({"proxyRelativePath":"../escape.jpg"}),
            lambda f:f["photos"][0].update({"proxySha256":"a"*6}),
            lambda f:f["photos"][1].update({"revisionId":IDS[0]}),
            lambda f:f["photos"][1]["evidence"].update({"proxyProfileId":"unknown"}),
        ):
            data=fixture()
            modification(data)
            with self.assertRaises(ValueError):
                s.build(data)

    def test_independent_contexts_are_off_by_default(self):
        with tempfile.TemporaryDirectory() as folder:
            session,_,manifest=local_session(folder)
            state=review.view(session,"archive-cleanup","all",0)
            self.assertTrue(state["scopeEnabled"])
            self.assertEqual(state["remaining"],3)
            self.assertTrue(state["photoUrl"].startswith("http://127.0.0.1:5080/photo/"))
            review.set_decision(session,"archive-cleanup",IDS[0],"consider-exclusion")
            self.assertEqual(session["state"]["decisions"]["archive-cleanup"][IDS[0]],
                             "consider-exclusion")
            self.assertEqual(session["state"]["decisions"]["collection-curation"],{})
            with self.assertRaises(ValueError):
                review.set_decision(session,"collection-curation",IDS[0],"keep")
            review.set_scope(session,"collection-curation",True)
            review.set_decision(session,"collection-curation",IDS[0],"dismiss")
            self.assertEqual(session["state"]["decisions"]["archive-cleanup"][IDS[0]],
                             "consider-exclusion")
            self.assertEqual(session["state"]["decisions"]["collection-curation"][IDS[0]],
                             "dismiss")
            report=s.aggregate(manifest,session["state"])
            self.assertEqual(report["byScope"]["archive-cleanup"]["considerExclusion"],1)
            self.assertEqual(report["byScope"]["collection-curation"]["dismiss"],1)
            self.assertEqual(report["exclusionsPerformed"],0)
            self.assertFalse(report["automaticPhotoOrCollectionChanges"])

    def test_restart_resume_and_binding(self):
        with tempfile.TemporaryDirectory() as folder:
            session,_,manifest=local_session(folder)
            review.set_decision(session,"archive-cleanup",IDS[1],"keep")
            resumed=review.session_load(
                session["state_path"].with_name("manifest.json"),
                session["state_path"],session["root"],"http://127.0.0.1:5080")
            self.assertEqual(review.view(resumed,"archive-cleanup","all",0)["reviewed"],1)
            bad=copy.deepcopy(manifest)
            bad["candidates"][0]["priority"]+=1
            with self.assertRaises(ValueError):
                s.validate_state(resumed["state"],bad)
            review.set_scope(resumed,"slideshow-curation",True)
            self.assertTrue(review.view(resumed,"slideshow-curation","all",0)["scopeEnabled"])

    def test_proxy_sha_symlink_and_path_containment(self):
        with tempfile.TemporaryDirectory() as folder:
            session,_,_=local_session(folder)
            sample=session["by_id"][IDS[0]]
            self.assertTrue(s.read_proxy(session["root"],sample))
            proxy=(session["root"]/sample["proxyRelativePath"])
            proxy.write_bytes(b"tampered")
            with self.assertRaises(ValueError):
                s.read_proxy(session["root"],sample)
            with self.assertRaises(ValueError):
                s.read_proxy(session["root"],{**sample,"proxyRelativePath":"../outside.jpg"})

    def test_no_endpoint_can_purge_or_modify_catalogue(self):
        content=review.HTML.lower()
        self.assertIn("finish & report",content)
        self.assertIn("explicit",content)
        self.assertNotIn("fetch(\"/api/photos",content)
        self.assertNotIn("exclude & purge selected",content)
        self.assertEqual(set(s.ACTIONS),{"keep","dismiss","consider-exclusion"})
        with tempfile.TemporaryDirectory() as folder:
            session,_,_=local_session(folder)
            with self.assertRaises(ValueError):
                review.set_decision(session,"archive-cleanup",IDS[0],"exclude")
            with self.assertRaises(ValueError):
                review.set_scope(session,"archive-cleanup","true")
            with self.assertRaises(ValueError):
                review.view(session,"unrecognized","all",0)

    def test_private_aggregate_and_reason_filters(self):
        with tempfile.TemporaryDirectory() as folder:
            session,_,manifest=local_session(folder)
            sharp=review.view(session,"archive-cleanup","candidate-low-sharpness",0)
            self.assertEqual(len(sharp["candidates"]),1)
            self.assertEqual(sharp["current"]["revisionId"],IDS[0])
            review.set_decision(session,"archive-cleanup",IDS[0],"dismiss")
            report=s.aggregate(manifest,session["state"])
            self.assertEqual(report["byScope"]["archive-cleanup"]["dismiss"],1)
            shown=json.dumps(report)
            for revision in IDS:
                self.assertNotIn(revision,shown)
            self.assertNotIn("private-source",shown)

    def test_nothing_written_before_operator_review(self):
        with tempfile.TemporaryDirectory() as folder:
            session,_,manifest=local_session(folder)
            self.assertFalse(session["state_path"].exists())
            _=review.view(session,"archive-cleanup","all",0)
            self.assertFalse(session["state_path"].exists())
            review.set_decision(session,"archive-cleanup",IDS[0],"keep")
            self.assertTrue(session["state_path"].exists())


if __name__=="__main__":
    unittest.main()
