//
//  ViewController.swift
//  test_for_Cuckoo
//
//  Created by wada on 2020/12/08.
//  Copyright © 2020 和田樹拓. All rights reserved.
//

import UIKit

class ViewController: UIViewController {

    @IBOutlet weak var scrollView: UIScrollView!
    var presenter: PresenterProtocol!
    override func viewDidLoad() {
        super.viewDidLoad()
        // Do any additional setup after loading the view.
        presenter = MainPresenter(vc: self)
        scrollView.contentSize.height = scrollView.frame.height
        scrollView.contentSize.width = scrollView.frame.width

    }


}

